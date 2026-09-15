using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
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
/// <b>P2-T12, "Do this" #3</b>: total inventory value change equals receipts (GRN cost value in)
/// minus COGS (cost of goods sold via completed sales, net of returns) plus adjustments
/// (positive/negative stock adjustments at cost) plus write-offs (damage) - to the cent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately a smaller, purpose-built dataset, not the 10 000-operation one
/// <see cref="StockConservationTests"/> builds.</b> The task's own brief allows either
/// ("this can likely run against the same 10 000-operation dataset ... or a smaller
/// hand-tractable one"). Two things the bigger dataset carries would make an exact,
/// to-the-cent reconciliation dishonest here, not just harder:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Bulk breaks and stock takes are excluded.</b> The formula this task names has exactly four
/// terms - receipts, COGS, adjustments, write-offs - and does not mention either. A bulk break's
/// destination unit cost is <c>totalValue / actualQuantity</c>
/// (<c>PostBulkBreakHandler</c>'s own remarks, and <c>BulkBreakTests</c>' own generative test):
/// an ordinary decimal division that cannot always reconstruct its dividend bit-for-bit once
/// quantised back to <see cref="Money"/>'s four decimal places, leaving a bounded but genuine
/// sub-cent residual <em>by design</em>, not by defect. Folding that residual into a strict
/// to-the-cent identity here would either force a tolerance wide enough to hide a real leak
/// elsewhere, or fail this test on nothing worse than the arithmetic
/// <c>BulkBreakTests</c> already prices in and re-proves separately.
/// A <c>STOCK_TAKE</c> correction's own value conservation is already proven by
/// <c>StockTakeServiceTests</c>' own variance-report assertions; there is nothing left for a third
/// test to add by including it here, at the cost of the same rounding risk.
/// </item>
/// <item>
/// <b>Every quantity in this test is a whole number of base units, and every GRN carries no tax
/// or freight.</b> Combined, this makes the reconciliation exact to the last unit of
/// <see cref="Money"/>'s own ×10 000 storage scale, not merely "close enough to the cent": a
/// quantity that is always an exact multiple of <see cref="Quantity.QtyScale"/> makes every
/// <c>qty_base × unit_cost</c> product an exact multiple of that same scale once divided back down,
/// with no fractional remainder for a tolerance to paper over. This is a stronger property than
/// the task asks for, achieved by construction rather than by relaxing the assertion.
/// </item>
/// </list>
/// <para>
/// <b>Each bucket is captured from the same authoritative source a report would read, never
/// re-derived from <c>stock_movement</c>.</b> Receipts come from
/// <see cref="GoodsReceiptResult.Receipt"/>'s own line totals; COGS comes from <c>sale.cogs</c>
/// (the snapshot <c>CompleteSaleHandler</c> writes) net of <c>sale_return_line</c>'s own
/// <c>SELLABLE</c>-disposition restock value; adjustments and write-offs come from
/// <see cref="AdjustmentResult"/>'s own <c>Delta</c>/<c>UnitCost</c>. If any of these four,
/// independently-computed figures ever drifted from what <see cref="IStockLedger"/> actually
/// posted - a COGS snapshot computed differently from the cost the ledger used, a return that
/// silently restocked at the wrong cost, an adjustment valued against a stale average - this test
/// would catch it as a reconciliation gap. Comparing <c>stock_movement</c> against itself would
/// not.
/// </para>
/// </remarks>
public sealed class ValueConservationTests
{
    private const int OperationCount = 400;
    private const int Seed = 20_260_915;
    private const int PoolSize = 6;

    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task P2_T12_TotalInventoryValueChangeReconcilesToReceiptsMinusCogsPlusAdjustmentsPlusWriteOffs()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        await ConfigureNumberSequencesAsync(fixture);
        var (variantIds, pieceUomId) = await SeedPoolAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);

        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var shiftId = await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

        var openingAt = new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.FromHours(5.5));

        // A hand-worked opening valuation: PoolSize variants, 1 000 pieces each at Rs 10.0000.
        const decimal OpeningQtyEach = 1_000m;
        const decimal OpeningCostEach = 10.0000m;
        foreach (var variantId in variantIds)
        {
            await PostOpeningStockAsync(fixture, variantId, pieceUomId, OpeningQtyEach, OpeningCostEach, openingAt);
        }

        var startValuation = await TotalValuationAsync(fixture, variantIds);
        startValuation.Should().Be(Money.FromDecimal(PoolSize * OpeningQtyEach * OpeningCostEach),
            "the opening valuation must match the hand-worked figure before anything else runs");

        var completeSale = fixture.Resolve<ICompleteSale>();
        var quoteSale = fixture.Resolve<IQuoteSale>();
        var createReturn = fixture.Resolve<ICreateReturn>();
        var goodsReceipt = fixture.Resolve<IGoodsReceiptService>();
        var postAdjustment = fixture.Resolve<IPostAdjustment>();

        var random = new Random(Seed);
        var clock = openingAt.AddHours(1);
        var openLines = new List<OpenSaleLine>();

        var receipts = Money.Zero;
        var cogs = Money.Zero;
        var adjustments = Money.Zero;
        var writeOffs = Money.Zero;

        for (var i = 0; i < OperationCount; i++)
        {
            clock = clock.AddSeconds(2);
            var roll = random.Next(1, 101);

            if (roll <= 55 || (roll <= 75 && openLines.Count == 0))
            {
                await DoSaleAsync();
            }
            else if (roll <= 75)
            {
                await DoReturnAsync();
            }
            else if (roll <= 90)
            {
                await DoGrnAsync();
            }
            else
            {
                await DoAdjustmentOrDamageAsync();
            }
        }

        var endValuation = await TotalValuationAsync(fixture, variantIds);

        var idList = string.Join(',', variantIds);
        var ledgerReceipts = Money.FromScaled(await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base*unit_cost),0)/" + Quantity.QtyScale + " FROM stock_movement WHERE movement_type='GRN' AND product_variant_id IN (" + idList + ");"));
        var ledgerSaleCogs = Money.FromScaled(await fixture.CountAsync(
            "SELECT COALESCE(-SUM(qty_base*unit_cost),0)/" + Quantity.QtyScale + " FROM stock_movement WHERE movement_type='SALE' AND product_variant_id IN (" + idList + ");"));
        var ledgerReturnIn = Money.FromScaled(await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base*unit_cost),0)/" + Quantity.QtyScale + " FROM stock_movement WHERE movement_type='RETURN_IN' AND product_variant_id IN (" + idList + ");"));
        var ledgerAdjustments = Money.FromScaled(await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base*unit_cost),0)/" + Quantity.QtyScale + " FROM stock_movement WHERE movement_type='ADJUSTMENT' AND product_variant_id IN (" + idList + ");"));
        var ledgerDamage = Money.FromScaled(await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base*unit_cost),0)/" + Quantity.QtyScale + " FROM stock_movement WHERE movement_type='DAMAGE' AND product_variant_id IN (" + idList + ");"));

        ledgerSaleCogs.Should().Be(Money.Zero, $"DEBUG receipts(test)={receipts} receipts(ledger)={ledgerReceipts} "
            + $"cogs(test)={cogs} saleCogs(ledger)={ledgerSaleCogs} returnIn(ledger)={ledgerReturnIn} netCogs(ledger)={ledgerSaleCogs - ledgerReturnIn} "
            + $"adjustments(test)={adjustments} adjustments(ledger)={ledgerAdjustments} "
            + $"writeOffs(test)={writeOffs} damage(ledger)={ledgerDamage}");

        var expectedChange = receipts - cogs + adjustments + writeOffs;
        var expectedEndValuation = startValuation + expectedChange;

        // Exact, not "within a cent" - see the class remarks for why this dataset's own
        // construction (whole-unit quantities, tax- and freight-free GRNs, no bulk break or
        // stock take) makes bit-exact the honest bar here, a strictly stronger proof than the
        // task's own "to the cent".
        endValuation.Should().Be(
            expectedEndValuation,
            $"end valuation ({endValuation}) must equal the opening valuation ({startValuation}) plus "
            + $"receipts ({receipts}) minus COGS ({cogs}) plus adjustments ({adjustments}) plus "
            + $"write-offs ({writeOffs}) - a gap here is a real defect in how one of those four "
            + "figures was computed, not a rounding artefact");

        async Task DoSaleAsync()
        {
            var variantId = variantIds[random.Next(variantIds.Count)];
            var qty = random.Next(1, 6);

            var lines = new List<SaleLineRequest> { new(variantId, qty) };
            var quote = await quoteSale.QuoteAsync(lines);
            var completed = await completeSale.CompleteAsync(new CompleteSaleCommand(
                userId, shiftId, clock, lines, [new TenderRequest(TenderTypes.Cash, quote.Total)]));

            var saleLineId = await fixture.CountAsync(
                "SELECT id FROM sale_line WHERE sale_id = " + completed.SaleId + " ORDER BY id LIMIT 1;");

            var saleCogsScaled = await fixture.CountAsync(
                "SELECT cogs FROM sale WHERE id = " + completed.SaleId + ";");
            cogs += Money.FromScaled(saleCogsScaled);

            openLines.Add(new OpenSaleLine(completed.SaleId, saleLineId, variantId, qty));
        }

        async Task DoReturnAsync()
        {
            var index = random.Next(openLines.Count);
            var entry = openLines[index];

            var qtyToReturn = random.Next(1, (int)entry.Remaining + 1);
            var disposition = random.Next(5) == 0 ? ReturnDisposition.Damaged : ReturnDisposition.Sellable;

            var created = await createReturn.CreateAsync(new CreateReturnCommand(
                entry.SaleId,
                userId,
                shiftId,
                clock,
                [new ReturnLineRequest(
                    entry.SaleLineId, Quantity.FromDecimal(qtyToReturn, entry.SaleLineId), disposition, "Random test return")],
                RefundMethod.Cash));

            // Only a SELLABLE line restocks (CreateReturnHandler's own remarks: a DAMAGED line
            // posts no stock movement at all) - so only a SELLABLE line's value ever reduces net
            // COGS. Read from sale_return_line, the same snapshot a shrinkage/return report would
            // read, never re-derived from stock_movement.
            var restockedValueScaled = await fixture.CountAsync(
                "SELECT COALESCE(SUM(qty_base * unit_cost) / " + Quantity.QtyScale
                + ", 0) FROM sale_return_line WHERE sale_return_id = " + created.SaleReturnId
                + " AND disposition = 'SELLABLE';");
            cogs -= Money.FromScaled(restockedValueScaled);

            entry.Remaining -= qtyToReturn;
            if (entry.Remaining <= 0)
            {
                openLines.RemoveAt(index);
            }
        }

        async Task DoGrnAsync()
        {
            var variantId = variantIds[random.Next(variantIds.Count)];
            var qty = random.Next(1, 30);
            var unitCost = Money.FromDecimal(Math.Round(random.Next(100, 5_000) / 100m, 2));

            // No tax, no freight (OtherCost: Zero) - see the class remarks: this is what keeps
            // every line's own LineTotal exactly equal to qty x unit cost, with nothing else
            // folded in for the reconciliation to have to account for separately.
            var result = await goodsReceipt.ReceiveAsync(new CreateGoodsReceiptCommand(
                supplierId, null, null, clock, Money.Zero, null,
                [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, qty, unitCost)]));

            foreach (var line in result.Receipt.Lines)
            {
                receipts += line.LineTotal;
            }
        }

        async Task DoAdjustmentOrDamageAsync()
        {
            var variantId = variantIds[random.Next(variantIds.Count)];

            if (random.Next(2) == 0)
            {
                var delta = random.Next(2) == 0 ? random.Next(1, 15) : -random.Next(1, 15);
                var result = await postAdjustment.AdjustAsync(
                    new AdjustmentCommand(variantId, delta, null, "Random test adjustment", clock));

                adjustments += result.UnitCost * result.Delta.Value;
            }
            else
            {
                var quantity = random.Next(1, 8);
                var result = await postAdjustment.WriteOffDamageAsync(
                    new DamageCommand(variantId, quantity, "Random test damage", clock));

                // AdjustmentResult.Delta is already negative for a write-off (PostAdjustmentHandler
                // turns the positive Quantity into the negative movement) - this is a stock
                // decrease, so it lands in write-offs with a negative sign, exactly as
                // stock_movement records it.
                writeOffs += result.UnitCost * result.Delta.Value;
            }
        }
    }

    /// <summary>
    /// <c>SUM(qty_base x cost_avg)</c> across every seeded variant, kept in the same combined
    /// ×10 000² scale the raw columns carry until the very last division - the same shape
    /// <c>P2-T11</c>'s own stock valuation query uses (its own remarks note the same
    /// C#-side-arithmetic convention), read directly rather than through
    /// <see cref="Counterpoint.Application.Inventory.IStockValuationQuery"/> so a cashier-session
    /// stripping rule elsewhere can never quietly zero out what this test is measuring.
    /// </summary>
    private static async Task<Money> TotalValuationAsync(SaleFixture fixture, IReadOnlyList<long> variantIds)
    {
        // Scoped to this test's own pool, deliberately: FirstRunSeeder's own skeleton product
        // also carries a stock_balance row, and this test's reconciliation has no opening figure
        // for it - summing every row in the table would silently fold an unrelated variant's
        // valuation into a comparison this test never accounted for.
        var idList = string.Join(',', variantIds);
        var totalRaw = await fixture.CountAsync(
            "SELECT COALESCE(SUM(qty_base * cost_avg), 0) / " + Quantity.QtyScale
            + " FROM stock_balance WHERE product_variant_id IN (" + idList + ");");

        return Money.FromScaled(totalRaw);
    }

    private static async Task ConfigureNumberSequencesAsync(SaleFixture fixture)
    {
        var sequences = fixture.Resolve<INumberSequenceConfiguration>();
        await sequences.ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);
    }

    private static async Task<(List<long> VariantIds, long PieceUomId)> SeedPoolAsync(SaleFixture fixture)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;
        var taxClassId = (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

        var variantIds = new List<long>();
        for (var i = 0; i < PoolSize; i++)
        {
            var code = "VC-" + i.ToString("000", CultureInfo.InvariantCulture);
            var productId = await products.CreateAsync(new SaveProductCommand(
                code,
                "Value conservation stock " + i.ToString(CultureInfo.InvariantCulture),
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
                productId, new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(20m)));

            variantIds.Add(variantId);
        }

        return (variantIds, pieceUomId);
    }

    private static async Task<long> SeedSupplierAsync(SaleFixture fixture) =>
        await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand("Value Conservation Supplier", null, null, null, null, null));

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost, DateTimeOffset at)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId, "OPENING", Quantity.FromDecimal(quantity, uomId), Money.FromDecimal(unitCost),
            "OPENING", RefDocId: null, userId, at));
    }

    private sealed class OpenSaleLine(long saleId, long saleLineId, long variantId, decimal remaining)
    {
        public long SaleId { get; } = saleId;

        public long SaleLineId { get; } = saleLineId;

        public long VariantId { get; } = variantId;

        public decimal Remaining { get; set; } = remaining;
    }
}
