using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Exchanges;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// <b>P2-T12, "Do this" #5</b>: a full simulated trading day - two ordinary sales, a standalone
/// return, an exchange, a GRN and a stock take, all through the real Application-layer services
/// against a real SQLite file - ending with hand-worked, exact end-state figures for stock
/// quantity, stock value and cash reconciliation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every quantity and cost in this scenario is chosen so the moving average never rounds.</b>
/// A GRN's own inbound value is <c>qty x unitCost</c>, exact by construction (no tax, no freight);
/// a restock from a return and a "found"/"missing" stock-take correction both post at whatever the
/// current moving-average cost already is (<c>CreateReturnHandler</c> restocks at the sale line's
/// own snapshot cost, which is unchanged since the sale; <c>StockTakeService.PostAsync</c>'s own
/// remarks: a correction is valued at the current <c>cost_avg</c>), so neither ever recomputes the
/// average against a *different* cost - the one case (<see cref="MovingAverageCost.Recompute"/>)
/// that can produce a storage-scale residual (see <c>ValueConservationTests</c>' own remarks for
/// where that residual comes from and why it is a real, by-design property of the costing model,
/// not a defect). The one GRN this scenario does post is deliberately sized so
/// <c>(190 x 10.00 + 10 x 29.00) / 200 = 10.95</c> lands exactly on a multiple of
/// <see cref="Money"/>'s own storage scale. The result is a trading day whose end-state figures
/// are provably exact, not merely "close enough" - a stronger bar than <c>ValueConservationTests</c>
/// sets itself, deliberately, because this test's whole purpose is to be hand-verifiable.
/// </para>
/// <para>
/// <b>Two independent routes to the same end valuation.</b> The bucket route (opening + GRN
/// receipts - net COGS + the stock take's own variance value) and the balance route
/// (<c>SUM(stock_balance.qty_base x cost_avg)</c>) are computed from different tables entirely and
/// asserted equal - the same cross-check <c>ValueConservationTests</c> runs, at a scale small
/// enough to also hand-verify by arithmetic in this class's own remarks.
/// </para>
/// </remarks>
public sealed class FullTradingDayReconciliationTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task P2_T12_AFullTradingDayWithAReturnAnExchangeAGrnAndAStockTakeEndsWithExactFigures()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureNumberSequencesAsync(fixture);

        var categoryId = await fixture.Resolve<ICategoryMaintenance>()
            .CreateAsync(new SaveCategoryCommand("Trading Day " + Guid.NewGuid().ToString("N")[..8], null));
        var pieceUomId = (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;
        var taxClassId = (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;
        var supplierId = await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand("Trading Day Supplier", null, null, null, null, null));

        var variantA = await SeedVariantAsync(fixture, "TD-A", categoryId, pieceUomId, taxClassId, sellingPrice: 15.00m);
        var variantB = await SeedVariantAsync(fixture, "TD-B", categoryId, pieceUomId, taxClassId, sellingPrice: 25.00m);

        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var shiftId = await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

        // Opening stock: 200 A @ 10.00 (2 000.00) and 100 B @ 20.00 (2 000.00) - 4 000.00 total.
        await PostOpeningStockAsync(fixture, variantA, pieceUomId, 200m, 10.00m, DayStart);
        await PostOpeningStockAsync(fixture, variantB, pieceUomId, 100m, 20.00m, DayStart);

        var quoteSale = fixture.Resolve<IQuoteSale>();
        var completeSale = fixture.Resolve<ICompleteSale>();

        // 09:15 - sale 1: 10 A + 5 B, one bill, cash in full.
        var sale1Lines = new List<SaleLineRequest> { new(variantA, 10m), new(variantB, 5m) };
        var sale1Quote = await quoteSale.QuoteAsync(sale1Lines);
        sale1Quote.Total.Should().Be(Money.FromDecimal(275.00m), "10 x 15.00 + 5 x 25.00 = 275.00");
        var sale1 = await completeSale.CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, DayStart.AddMinutes(15), sale1Lines,
            [new TenderRequest(TenderTypes.Cash, sale1Quote.Total)]));
        var sale1LineA = await fixture.CountAsync(
            "SELECT id FROM sale_line WHERE sale_id = " + sale1.SaleId + " AND product_variant_id = " + variantA + ";");

        // 09:30 - a standalone return of 3 of the 10 A, sellable, refunded at the price paid
        // (AC-03), restocked at the same cost that was on the shelf at the time of the sale.
        var standaloneReturn = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale1.SaleId, userId, shiftId, DayStart.AddMinutes(30),
            [new ReturnLineRequest(sale1LineA, Quantity.FromDecimal(3m, sale1LineA), ReturnDisposition.Sellable, "Bought too many")],
            RefundMethod.Cash));
        standaloneReturn.TotalRefund.Should().Be(
            Money.FromDecimal(45.00m), "3 x 15.00, the price actually paid - no restocking fee configured");

        // 09:45 - sale 2: 4 B, cash in full - the bill the exchange below is taken against.
        var sale2Lines = new List<SaleLineRequest> { new(variantB, 4m) };
        var sale2Quote = await quoteSale.QuoteAsync(sale2Lines);
        var sale2 = await completeSale.CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, DayStart.AddMinutes(45), sale2Lines,
            [new TenderRequest(TenderTypes.Cash, sale2Quote.Total)]));
        var sale2LineB = await fixture.CountAsync(
            "SELECT id FROM sale_line WHERE sale_id = " + sale2.SaleId + " AND product_variant_id = " + variantB + ";");

        // 10:00 - an exchange: 2 B back (worth 50.00 at the price paid) for 3 A (worth 45.00 new) -
        // a lower-priced replacement, so the 5.00 surplus is refunded for real (the same shape
        // CreateExchangeTests' own "lower priced replacement" case proves).
        var exchange = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale2.SaleId, userId, shiftId, DayStart.AddHours(1),
            [new ReturnLineRequest(sale2LineB, Quantity.FromDecimal(2m, sale2LineB), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantA, 3m)],
            DifferenceTenders: []));

        exchange.ReturnValue.Should().Be(Money.FromDecimal(50.00m), "2 x 25.00, the price actually paid for B");
        exchange.ReplacementTotal.Should().Be(Money.FromDecimal(45.00m), "3 x 15.00 new A");
        exchange.CreditApplied.Should().Be(Money.FromDecimal(45.00m));
        exchange.AmountCollected.Should().Be(Money.Zero);
        exchange.RefundPaid.Should().Be(Money.FromDecimal(5.00m), "50.00 - 45.00 surplus, paid back for real");

        // 11:00 - a GRN for A only: 10 more @ 29.00 (290.00), chosen so the new moving average
        // divides evenly - (190 x 10.00 + 10 x 29.00) / 200 = 10.95 exactly - so this test's own
        // hand-worked figures never have to account for the storage-scale rounding
        // ValueConservationTests deliberately does not control for (see this class's own remarks).
        var grn = await fixture.Resolve<IGoodsReceiptService>().ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, DayStart.AddHours(2), Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantA, pieceUomId, 10m, Money.FromDecimal(29.00m))]));
        grn.Receipt.Lines.Single().LineTotal.Should().Be(Money.FromDecimal(290.00m));

        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantA + ";"))
            .Should().Be(
                Money.FromDecimal(10.95m).ToScaled().ToString(CultureInfo.InvariantCulture),
                "(190 x 10.00 + 10 x 29.00) / 200 = 10.95 exactly");

        // 17:00 - end-of-day stock take across both variants: A comes up 5 short (shrinkage), B
        // comes up 2 over (a miscount caught and corrected). Both corrections post at whatever the
        // *current* moving-average cost already is (StockTakeService.PostAsync's own remarks), so
        // neither one moves the average - only the quantity - which is why this test's own hand
        // arithmetic never has to solve a division for either of them.
        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand(
            "CATEGORY:" + categoryId.ToString(CultureInfo.InvariantCulture), DayStart.AddHours(8)));
        started.LineCount.Should().Be(2, "only the two variants seeded into this category");

        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantA, 195m));
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantB, 95m));

        var varianceReport = await stockTakes.BuildVarianceReportAsync(started.StockTakeId);
        varianceReport.Lines.Should().HaveCount(2);
        varianceReport.Lines[0].ProductVariantId.Should().Be(
            variantA, "sorted by absolute value impact descending: 54.75 before 40.00");
        varianceReport.Lines[0].Variance!.Value.Value.Should().Be(-5m);
        varianceReport.Lines[0].Value.Should().Be(Money.FromDecimal(-54.75m), "5 x 10.95, the current average");
        varianceReport.Lines[1].ProductVariantId.Should().Be(variantB);
        varianceReport.Lines[1].Variance!.Value.Value.Should().Be(2m);
        varianceReport.Lines[1].Value.Should().Be(Money.FromDecimal(40.00m), "2 x 20.00, the current average");
        varianceReport.TotalValue.Should().Be(Money.FromDecimal(-14.75m), "-54.75 + 40.00");

        var posted = await stockTakes.PostAsync(
            new PostStockTakeCommand(started.StockTakeId, DayStart.AddHours(8).AddMinutes(30)));
        posted.MovementsPosted.Should().Be(2, "both lines carried a non-zero variance");
        posted.LinesSkipped.Should().Be(0);

        // ---- End-state figures ----

        // Stock quantities: exactly what the day's own arithmetic says.
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantA + ";"))
            .Should().Be(
                Quantity.FromDecimal(195m, pieceUomId).ToScaled().ToString(CultureInfo.InvariantCulture),
                "200 - 10 (sale 1) + 3 (return) - 3 (exchange) + 10 (GRN) - 5 (stock take) = 195");
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantB + ";"))
            .Should().Be(
                Quantity.FromDecimal(95m, pieceUomId).ToScaled().ToString(CultureInfo.InvariantCulture),
                "100 - 5 (sale 1) - 4 (sale 2) + 2 (exchange) + 2 (stock take) = 95");

        // Stock conservation (CLAUDE.md invariant 3): the balance projection must equal the ledger
        // sum for both variants - the same check StockConservationTests runs at 10 000-operation
        // scale, reused here rather than re-derived.
        var consistency = await fixture.Resolve<IStockConsistencyCheck>().CheckAsync(sampleSize: 10);
        consistency.Mismatches.Should().BeEmpty();

        // Stock value, two independent routes to the same number.
        var endValuation = await TotalValuationAsync(fixture, variantA, variantB);
        endValuation.Should().Be(Money.FromDecimal(4_035.25m), "195 x 10.95 + 95 x 20.00 = 2 135.25 + 1 900.00");

        var openingValuation = Money.FromDecimal(4_000.00m);
        var receipts = Money.FromDecimal(290.00m);
        var sale1Cogs = Money.FromScaled(await fixture.CountAsync("SELECT cogs FROM sale WHERE id = " + sale1.SaleId + ";"));
        var sale2Cogs = Money.FromScaled(await fixture.CountAsync("SELECT cogs FROM sale WHERE id = " + sale2.SaleId + ";"));
        var exchangeSaleCogs = Money.FromScaled(await fixture.CountAsync("SELECT cogs FROM sale WHERE id = " + exchange.SaleId + ";"));
        var restockedValue = Money.FromScaled(await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base * unit_cost), 0) / " + Quantity.QtyScale
            + " FROM sale_return_line WHERE disposition = 'SELLABLE';"));
        var netCogs = sale1Cogs + sale2Cogs + exchangeSaleCogs - restockedValue;
        netCogs.Should().Be(Money.FromDecimal(240.00m), "310.00 total sale cogs (200 + 80 + 30) - 70.00 restocked (30 + 40)");

        var stockTakeValue = varianceReport.TotalValue!.Value;
        (openingValuation + receipts - netCogs + stockTakeValue).Should().Be(
            endValuation,
            "opening (4 000.00) + GRN receipts (290.00) - net COGS (240.00) + the stock take's own "
            + "variance value (-14.75) must equal the end valuation (4 035.25) exactly - both routes "
            + "read from different tables entirely, so a gap here is a real reconciliation defect, "
            + "not a rounding artefact (this scenario is deliberately built to carry none)");

        // Cash reconciliation: net sales (every COMPLETED sale row's own total, including the
        // exchange's own sale at its full replacement value - ExchangeTenderType0010, never netted
        // by the credit) must equal sum(payment) on the sale side, and the refund payments must net
        // out to exactly what was paid back.
        var netSalesTotal = Money.FromScaled(await fixture.CountAsync("SELECT SUM(total) FROM sale WHERE status = 'COMPLETED';"));
        netSalesTotal.Should().Be(
            Money.FromDecimal(420.00m),
            "275.00 (sale 1) + 100.00 (sale 2) + 45.00 (the exchange's own sale, its full replacement value)");

        var tenderedOnSales = Money.FromScaled(
            await fixture.CountAsync("SELECT COALESCE(SUM(amount), 0) FROM payment WHERE sale_id IS NOT NULL;"));
        tenderedOnSales.Should().Be(netSalesTotal, "sum(payment) on the sale side equals sum(sale.total) exactly - including the exchange sale's own 45.00 EXCHANGE tender");

        // The exchange's own return now also carries its EXCHANGE credit payment, the exact
        // negative of the sale's own EXCHANGE tender above - not a real refund, but still part of
        // sum(payment) for that document (CreateExchangeHandler's own remarks).
        var refundPayments = Money.FromScaled(
            await fixture.CountAsync("SELECT COALESCE(SUM(amount), 0) FROM payment WHERE sale_return_id IS NOT NULL;"));
        refundPayments.Should().Be(
            Money.FromDecimal(-95.00m),
            "-45.00 (standalone return) + -45.00 (the exchange's EXCHANGE credit, mirroring its sale) + -5.00 (exchange surplus)");

        // Real cash movement only, once the EXCHANGE bucket's equal-and-opposite entries on the two
        // sides above are added together - unchanged by ExchangeTenderType0010, since it only moved
        // where the credit is recorded, never what actually left or stayed in the till.
        var netCashRetained = tenderedOnSales + refundPayments;
        netCashRetained.Should().Be(
            Money.FromDecimal(325.00m),
            "230.00 (7 A + 5 B kept from sale 1/the return) + 95.00 (2 B + 3 A kept from sale 2/the exchange)");
    }

    /// <summary>
    /// <c>SUM(qty_base x cost_avg)</c> across just the two variants this scenario seeded - the same
    /// combined ×10 000² scale kept until the final division that <c>ValueConservationTests</c>'
    /// own valuation query uses, scoped so <c>FirstRunSeeder</c>'s own skeleton product (which
    /// carries no opening figure for this scenario to account for) is never silently folded in.
    /// </summary>
    private static async Task<Money> TotalValuationAsync(SaleFixture fixture, long variantA, long variantB)
    {
        var totalRaw = await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base * cost_avg), 0) / " + Quantity.QtyScale
            + " FROM stock_balance WHERE product_variant_id IN (" + variantA + "," + variantB + ");");

        return Money.FromScaled(totalRaw);
    }

    private static async Task<long> SeedVariantAsync(
        SaleFixture fixture, string code, long categoryId, long pieceUomId, long taxClassId, decimal sellingPrice)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Trading day " + code,
            NameAlt: null,
            CategoryId: categoryId,
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

        return await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(sellingPrice)));
    }

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost, DateTimeOffset at)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId, "OPENING", Quantity.FromDecimal(quantity, uomId), Money.FromDecimal(unitCost),
            "OPENING", RefDocId: null, userId, at));
    }

    private static async Task ConfigureNumberSequencesAsync(SaleFixture fixture)
    {
        var sequences = fixture.Resolve<INumberSequenceConfiguration>();
        await sequences.ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("STOCK_TAKE", "ST-", "{prefix}{yyyy}-{n:000000}", 1);
    }
}
