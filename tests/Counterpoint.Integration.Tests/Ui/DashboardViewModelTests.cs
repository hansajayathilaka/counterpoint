using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels.Dashboard;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// Task P3-T20's own "Done when" list (SRS FR-9.7, UI-16): <see cref="DashboardViewModel"/> is a
/// thin pass-through of three existing queries (<see cref="IDashboardQueries"/>,
/// <see cref="IReorderListQuery"/>, <see cref="IRecentSalesQuery"/>) - every one of these proves
/// the viewmodel's own figures match what those queries themselves return on the same seeded real
/// SQLite database (<see cref="SaleFixture"/>), never the in-memory EF Core provider.
/// </summary>
public sealed class DashboardViewModelTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    // ---- Done when #1: the four KPI cards -------------------------------------------------------

    [Fact]
    public async Task FR_9_7_KpiCardsMatchIDashboardQueriesGetSummaryAsyncOnASeededDataset()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        // The exact seed DashboardServiceTests itself uses: a real opening float, one cash bill,
        // one card bill (only the cash one belongs in the drawer), and a product pushed at or
        // below its reorder level.
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var opened = await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(user.Id, Money.FromDecimal(5000m), SoldAt));

        await CompleteOneAsync(fixture, opened.ShiftId, user.Id, quantity: 1m, TenderTypes.Cash); // 12.50
        await CompleteOneAsync(fixture, opened.ShiftId, user.Id, quantity: 2m, TenderTypes.Card); // 25.00

        await fixture.ExecuteAsync("UPDATE product SET reorder_level = 2000000 WHERE code = 'SKEL-001';");

        var summary = await fixture.Resolve<IDashboardQueries>().GetSummaryAsync();
        summary.TodaysSales.Should().Be(Money.FromDecimal(37.50m));
        summary.BillCount.Should().Be(2);
        summary.CashInDrawer.Should().Be(Money.FromDecimal(5012.50m));
        summary.LowStockCount.Should().Be(1);

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        // Not independently recomputed - the KPI text is exactly DashboardSummary's own fields
        // formatted, nothing approximated or hard-coded (task P3-T20's own named risk).
        dashboard.TodaysSalesText.Should().Be(summary.TodaysSales.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        dashboard.BillCountText.Should().Be(summary.BillCount.ToString(CultureInfo.InvariantCulture));
        dashboard.LowStockCountText.Should().Be(summary.LowStockCount.ToString(CultureInfo.InvariantCulture));
        dashboard.LowStockCount.Should().Be(summary.LowStockCount);
        dashboard.CashInDrawerText.Should().Be(summary.CashInDrawer!.Value.Amount.ToString("0.00", CultureInfo.InvariantCulture));

        // And the concrete figures too, so a future change to DashboardService's own arithmetic
        // that happened to agree with itself but drift from the shop's expectation would still be
        // caught here, the same way DashboardServiceTests itself asserts concrete numbers.
        dashboard.TodaysSalesText.Should().Be("37.50");
        dashboard.BillCountText.Should().Be("2");
        dashboard.LowStockCountText.Should().Be("1");
        dashboard.CashInDrawerText.Should().Be("5012.50");
    }

    [Fact]
    public async Task FR_9_7_CashInDrawerTextReadsNoShiftOpenWhenTheDashboardSummaryCarriesNoCashFigure()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        await fixture.SignInAsSeededOwnerAsync();

        var summary = await fixture.Resolve<IDashboardQueries>().GetSummaryAsync();
        summary.CashInDrawer.Should().BeNull("no shift is open");

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.CashInDrawerText.Should().Be("No shift open");
    }

    // ---- Done when #2: the reorder-alerts panel --------------------------------------------------

    [Fact]
    public async Task FR_4_ReorderAlertsPanelMatchesIReorderListQueryOnTheSameSeededDataset()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);

        var (belowVariantId, belowProductId, pieceUomId) =
            await SeedProductWithVariantAsync(fixture, "DASH-REORDER-LOW");
        var (aboveVariantId, aboveProductId, _) =
            await SeedProductWithVariantAsync(fixture, "DASH-REORDER-HIGH");
        var (withSupplierVariantId, withSupplierProductId, supplierUomId) =
            await SeedProductWithVariantAsync(fixture, "DASH-REORDER-SUPPLIER");

        await SetReorderLevelsAsync(fixture, belowProductId, pieceUomId, level: 20m, qty: 50m);
        await PostOpeningStockAsync(fixture, belowVariantId, pieceUomId, 5m, 1.00m);

        await SetReorderLevelsAsync(fixture, aboveProductId, pieceUomId, level: 20m, qty: 50m);
        await PostOpeningStockAsync(fixture, aboveVariantId, pieceUomId, 500m, 1.00m);

        await SetReorderLevelsAsync(fixture, withSupplierProductId, supplierUomId, level: 20m, qty: 50m);
        await PostOpeningStockAsync(fixture, withSupplierVariantId, supplierUomId, 5m, 1.00m);
        var supplierId = await SeedSupplierAsync(fixture, "Dashboard Test Supplier");
        await ReceiveAsync(fixture, supplierId, withSupplierVariantId, supplierUomId, qty: 10m, unitCost: 1.50m, ReceivedAt);

        var expected = await fixture.Resolve<IReorderListQuery>().GetReorderListAsync();
        expected.Should().HaveCountGreaterThanOrEqualTo(2, "the seed above put at least two products at or below their reorder level");

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.HasReorderAlerts.Should().BeTrue();
        dashboard.ReorderAlerts.Should().HaveCount(expected.Count);

        // Same members, same order - IReorderListQuery's own "furthest under level first" order,
        // never re-sorted by the viewmodel.
        dashboard.ReorderAlerts.Select(row => row.ProductCode).Should().Equal(expected.Select(line => line.ProductCode));

        foreach (var line in expected)
        {
            var row = dashboard.ReorderAlerts.Single(r => r.ProductCode == line.ProductCode);

            row.ProductDescription.Should().Be(line.ProductDescription);
            row.OnHandText.Should().Be(FormatQty(line.QtyOnHandBase, line.BaseUomSymbol));
            row.ReorderLevelText.Should().Be(FormatQty(line.ReorderLevel, line.BaseUomSymbol));
            row.SuggestedQtyText.Should().Be(FormatQty(line.SuggestedQty, line.BaseUomSymbol));
            row.SupplierText.Should().Be(line.PreferredSupplierName ?? "(none linked)");
        }

        // The two concrete cases seeded above, spelled out - not just "the loop agreed with
        // itself".
        dashboard.ReorderAlerts.Should().NotContain(r => r.ProductCode == "DASH-REORDER-HIGH",
            "500 pieces on hand is comfortably above the 20-piece reorder level");
        dashboard.ReorderAlerts.Single(r => r.ProductCode == "DASH-REORDER-LOW").SupplierText
            .Should().Be("(none linked)");
        dashboard.ReorderAlerts.Single(r => r.ProductCode == "DASH-REORDER-SUPPLIER").SupplierText
            .Should().Be("Dashboard Test Supplier");
    }

    [Fact]
    public async Task FR_4_ReorderAlertsPanelIsEmptyAndHasReorderAlertsIsFalseWhenNothingIsLow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.HasReorderAlerts.Should().BeFalse("the seeded product's reorder_level is the default zero - not tracked");
        dashboard.ReorderAlerts.Should().BeEmpty();
    }

    // ---- Done when #3: the recent-sales list ------------------------------------------------------

    [Fact]
    public async Task FR_9_7_RecentSalesListMatchesIRecentSalesQueryOnTheSameSeededDataset()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var customerId = await fixture.Resolve<ICustomerMaintenance>().CreateAsync(
            new SaveCustomerCommand("Dashboard Test Customer", null, null, null, "RETAIL", Money.Zero));

        var bill1 = new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.FromHours(5.5));
        var bill2 = bill1.AddHours(1);
        var bill3 = bill1.AddHours(2);

        var sale1 = await CompleteBoltSaleAsync(fixture, 1m, bill1, customerId: null);
        var sale2 = await CompleteBoltSaleAsync(fixture, 2m, bill2, customerId: customerId);
        var sale3 = await CompleteBoltSaleAsync(fixture, 3m, bill3, customerId: null);

        var expected = await fixture.Resolve<IRecentSalesQuery>().GetRecentAsync(8);
        expected.Should().HaveCount(3);

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.HasRecentSales.Should().BeTrue();
        dashboard.RecentSales.Should().HaveCount(expected.Count);
        dashboard.RecentSales.Select(row => row.BillNo).Should().Equal(expected.Select(sale => sale.BillNo));

        for (var i = 0; i < expected.Count; i++)
        {
            dashboard.RecentSales[i].BillNo.Should().Be(expected[i].BillNo);
            dashboard.RecentSales[i].CompletedAtText.Should().Be(
                expected[i].CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            dashboard.RecentSales[i].CustomerName.Should().Be(expected[i].CustomerName);
            dashboard.RecentSales[i].TotalText.Should().Be(
                expected[i].Total.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        }

        // Most recent first, and the one bill with a real customer carries that name, not
        // "Walk-in" - spelled out concretely, not just "the loop agreed with itself".
        dashboard.RecentSales[0].BillNo.Should().Be(sale3.BillNo);
        dashboard.RecentSales.Should().ContainSingle(
            row => row.BillNo == sale2.BillNo && row.CustomerName == "Dashboard Test Customer");
        dashboard.RecentSales.Should().Contain(row => row.BillNo == sale1.BillNo && row.CustomerName == "Walk-in");
    }

    [Fact]
    public async Task FR_9_7_RecentSalesListNeverShowsACancelledSale()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var kept = await CompleteBoltSaleAsync(fixture, 1m, SoldAt, customerId: null);
        var cancelled = await CompleteBoltSaleAsync(fixture, 2m, SoldAt.AddMinutes(5), customerId: null);

        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(cancelled.SaleId, "Rung up in error", SoldAt.AddMinutes(10)));

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.RecentSales.Select(row => row.BillNo).Should().Contain(kept.BillNo);
        dashboard.RecentSales.Select(row => row.BillNo).Should().NotContain(
            cancelled.BillNo, "a cancelled bill must never appear on the Overview's recent-sales list");
    }

    [Fact]
    public async Task FR_9_7_RecentSalesListIsEmptyAndHasRecentSalesIsFalseBeforeAnyBillIsCompleted()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.HasRecentSales.Should().BeFalse();
        dashboard.RecentSales.Should().BeEmpty();
    }

    // ---- The low-stock KPI card's own danger-tint trigger (task P3-T20 "Do this" #5) ------------

    [Fact]
    public async Task UI_16_HasLowStockAlertsTracksLowStockCountCrossingZero()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var dashboard = BuildDashboard(fixture);
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.LowStockCount.Should().Be(0);
        dashboard.HasLowStockAlerts.Should().BeFalse();

        await fixture.ExecuteAsync("UPDATE product SET reorder_level = 2000000 WHERE code = 'SKEL-001';");
        await dashboard.LoadCommand.ExecuteAsync(null);

        dashboard.LowStockCount.Should().Be(1);
        dashboard.HasLowStockAlerts.Should().BeTrue("the low-stock KPI card's danger tint binds straight off this flag");
    }

    // ---- Shared seeding / building ------------------------------------------------------------

    private static DashboardViewModel BuildDashboard(SaleFixture fixture) => new(
        fixture.Resolve<IDashboardQueries>(),
        fixture.Resolve<IReorderListQuery>(),
        fixture.Resolve<IRecentSalesQuery>());

    private static string FormatQty(Quantity quantity, string uomSymbol) =>
        quantity.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + uomSymbol;

    private static async Task<CompletedSale> CompleteOneAsync(
        SaleFixture fixture, long shiftId, long userId, decimal quantity, string tenderType)
    {
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                userId,
                shiftId,
                SoldAt,
                lines,
                [new TenderRequest(tenderType, quote.Total)]));
    }

    private static async Task<CompletedSale> CompleteBoltSaleAsync(
        SaleFixture fixture, decimal quantity, DateTimeOffset soldAt, long? customerId)
    {
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            shiftId,
            soldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)],
            customerId));
    }

    private static Task<bool> ConfigureGrnSeriesAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeedSupplierAsync(SaleFixture fixture, string name) =>
        await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand(name, null, null, null, null, null));

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

    private static async Task<(long VariantId, long ProductId, long PieceUomId)> SeedProductWithVariantAsync(
        SaleFixture fixture, string code)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Product " + code,
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            pieceUomId,
            ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null,
            ConfirmDuplicate: true));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(1.00m)));

        return (variantId, productId, pieceUomId);
    }

    private static Task SetReorderLevelsAsync(SaleFixture fixture, long productId, long pieceUomId, decimal level, decimal qty) =>
        fixture.ExecuteAsync(
            "UPDATE product SET reorder_level = " + Quantity.FromDecimal(level, pieceUomId).ToScaled()
            + ", reorder_qty = " + Quantity.FromDecimal(qty, pieceUomId).ToScaled()
            + " WHERE id = " + productId + ";");

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(quantity, uomId),
            Money.FromDecimal(unitCost),
            "OPENING",
            RefDocId: null,
            userId,
            ReceivedAt));
    }

    private static async Task ReceiveAsync(
        SaleFixture fixture,
        long supplierId,
        long variantId,
        long uomId,
        decimal qty,
        decimal unitCost,
        DateTimeOffset receivedAt)
    {
        await fixture.Resolve<IGoodsReceiptService>().ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId,
            PurchaseOrderId: null,
            SupplierInvoiceNo: null,
            receivedAt,
            OtherCost: Money.Zero,
            Note: null,
            [new CreateGoodsReceiptLineCommand(variantId, uomId, qty, Money.FromDecimal(unitCost))]));
    }
}
