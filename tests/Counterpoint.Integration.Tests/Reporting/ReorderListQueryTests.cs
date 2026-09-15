using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Purchasing;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The reorder alert list (task P2-T11 "Do this" #1), through the real SQLite database
/// <see cref="SaleFixture"/> composes - the same container the application runs, never the
/// in-memory provider.
/// </summary>
public sealed class ReorderListQueryTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task FR_4_TheReorderListMatchesAHandComputedExpectationOnSeededData()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (belowVariantId, belowProductId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "REORDER-LOW");
        var (aboveVariantId, aboveProductId, _) = await SeedProductWithVariantAsync(fixture, "REORDER-HIGH");

        await SetReorderLevelsAsync(fixture, belowProductId, pieceUomId, level: 20m, qty: 50m);
        await PostOpeningStockAsync(fixture, belowVariantId, pieceUomId, 5m, 1.00m);

        await SetReorderLevelsAsync(fixture, aboveProductId, pieceUomId, level: 20m, qty: 50m);
        await PostOpeningStockAsync(fixture, aboveVariantId, pieceUomId, 500m, 1.00m);

        var list = await fixture.Resolve<IReorderListQuery>().GetReorderListAsync();

        var low = list.Should().ContainSingle(line => line.ProductId == belowProductId).Subject;
        low.QtyOnHandBase.Value.Should().Be(5m);
        low.ReorderLevel.Value.Should().Be(20m);
        low.SuggestedQty.Value.Should().Be(50m);
        low.BaseUomSymbol.Should().Be("pc");

        // Task P2-T11's own preferred-supplier note: no product_supplier row yet, so the product
        // still appears, with no supplier proposed.
        low.PreferredSupplierId.Should().BeNull();
        low.PreferredSupplierName.Should().BeNull();

        list.Should().NotContain(line => line.ProductId == aboveProductId);
    }

    [Fact]
    public async Task PreferredSupplier_ExactlyOneLinkedSupplierIsPreferred()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);

        var (variantId, productId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "REORDER-ONE-SUPPLIER");
        await SetReorderLevelsAsync(fixture, productId, pieceUomId, level: 20m, qty: 50m);
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 5m, 1.00m);

        var supplierId = await SeedSupplierAsync(fixture, "Sole Supplier Ltd");
        await ReceiveAsync(fixture, supplierId, variantId, pieceUomId, qty: 10m, unitCost: 1.50m, ReceivedAt);

        var line = (await fixture.Resolve<IReorderListQuery>().GetReorderListAsync())
            .Should().ContainSingle(l => l.ProductId == productId).Subject;

        line.PreferredSupplierId.Should().Be(supplierId);
        line.PreferredSupplierName.Should().Be("Sole Supplier Ltd");
    }

    [Fact]
    public async Task PreferredSupplier_MostRecentGoodsReceiptWinsAmongSeveralLinkedSuppliers()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);

        // A high reorder level, so the three goods receipts below (30 pieces in total) never
        // themselves lift the product out of the low-stock list this test is about - only the
        // preferred-supplier column is under test here.
        var (variantId, productId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "REORDER-TWO-SUPPLIERS");
        await SetReorderLevelsAsync(fixture, productId, pieceUomId, level: 1000m, qty: 500m);
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 5m, 1.00m);

        var earlySupplierId = await SeedSupplierAsync(fixture, "Early Supplier");
        var lateSupplierId = await SeedSupplierAsync(fixture, "Late Supplier");

        var earlyDate = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.FromHours(5.5));
        var lateDate = new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.FromHours(5.5));

        await ReceiveAsync(fixture, earlySupplierId, variantId, pieceUomId, 10m, 1.50m, earlyDate);
        await ReceiveAsync(fixture, lateSupplierId, variantId, pieceUomId, 10m, 1.60m, lateDate);

        var afterTwo = (await fixture.Resolve<IReorderListQuery>().GetReorderListAsync())
            .Should().ContainSingle(l => l.ProductId == productId).Subject;
        afterTwo.PreferredSupplierId.Should().Be(
            lateSupplierId, "the late supplier's goods receipt is the more recent of the two");

        // A later receipt from the *earlier* supplier flips the preference back - this is about
        // the most recent receipt, not which supplier was linked first.
        var latestDate = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(5.5));
        await ReceiveAsync(fixture, earlySupplierId, variantId, pieceUomId, 10m, 1.55m, latestDate);

        var afterThree = (await fixture.Resolve<IReorderListQuery>().GetReorderListAsync())
            .Should().ContainSingle(l => l.ProductId == productId).Subject;
        afterThree.PreferredSupplierId.Should().Be(earlySupplierId);
    }

    [Fact]
    public async Task PreferredSupplier_TiesBreakByTheLowestSupplierIdWhenNeitherLinkedSupplierHasAnyGoodsReceiptHistory()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (_, productId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "REORDER-NO-HISTORY");
        await SetReorderLevelsAsync(fixture, productId, pieceUomId, level: 20m, qty: 50m);

        var supplierAId = await SeedSupplierAsync(fixture, "Supplier A");
        var supplierBId = await SeedSupplierAsync(fixture, "Supplier B");

        // Neither supplier has ever supplied a goods receipt against this product - the task's
        // own preferred-supplier design note calls this out explicitly as a tie-break case, and
        // the only way to reach it today (goods receipt is the only writer of product_supplier)
        // is a direct link, the same way a later "explicit preferred supplier" screen would write
        // one without a receipt behind it yet.
        var lower = Math.Min(supplierAId, supplierBId);
        await fixture.ExecuteAsync(
            "INSERT INTO product_supplier (product_id, supplier_id) VALUES "
            + "(" + productId + ", " + supplierAId + "), (" + productId + ", " + supplierBId + ");");

        var line = (await fixture.Resolve<IReorderListQuery>().GetReorderListAsync())
            .Should().ContainSingle(l => l.ProductId == productId).Subject;

        line.PreferredSupplierId.Should().Be(lower, "the tie-break is purely for a deterministic result, not a purchasing judgement");
    }

    [Fact]
    public async Task TheDashboardsLowStockCountMatchesTheReorderListsOwnCount()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        for (var i = 0; i < 3; i++)
        {
            var (variantId, productId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "REORDER-DASH-LOW-" + i);
            await SetReorderLevelsAsync(fixture, productId, pieceUomId, level: 20m, qty: 50m);
            await PostOpeningStockAsync(fixture, variantId, pieceUomId, 1m, 1.00m);
        }

        for (var i = 0; i < 2; i++)
        {
            var (variantId, productId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "REORDER-DASH-HIGH-" + i);
            await SetReorderLevelsAsync(fixture, productId, pieceUomId, level: 20m, qty: 50m);
            await PostOpeningStockAsync(fixture, variantId, pieceUomId, 999m, 1.00m);
        }

        var reorderList = await fixture.Resolve<IReorderListQuery>().GetReorderListAsync();
        var dashboard = await fixture.Resolve<IDashboardQueries>().GetSummaryAsync();

        dashboard.LowStockCount.Should().Be(reorderList.Count);
    }

    // ---- Shared seeding -----------------------------------------------------------------------

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
