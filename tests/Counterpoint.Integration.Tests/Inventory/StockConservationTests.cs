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
/// <b>P2-T12, "Do this" #2</b>: after 10 000 random operations - sales, returns, GRNs,
/// adjustments, bulk breaks and stock takes - <c>stock_balance</c> must equal the ledger sum
/// (<c>SUM(stock_movement.qty_base)</c> grouped by variant) for every variant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every operation goes through the real Application-layer service</b> - <see cref="ICompleteSale"/>,
/// <see cref="ICreateReturn"/>, <see cref="IGoodsReceiptService"/>, <see cref="IPostAdjustment"/>,
/// <see cref="IPostBulkBreak"/>, <see cref="IStockTakeService"/> - never a direct write to
/// <c>stock_movement</c> or <c>stock_balance</c>. Writing either table directly would prove
/// nothing except that this test's own SQL agrees with itself; the whole point of the check is
/// that <em>the application</em>, exercised the way a till exercises it, never leaves the two
/// tables disagreeing (CLAUDE.md invariant 3).
/// </para>
/// <para>
/// <b>The check itself is <see cref="IStockConsistencyCheck"/>, not bespoke SQL.</b> That query
/// already is "compare the balance projection against <c>SUM(stock_movement.qty_base)</c> grouped
/// by variant" (P1-T07, SAD §3) - reusing it, at a sample size that covers every variant this test
/// touches, is more honest than a second, independently written copy of the same comparison that
/// could quietly drift from the real one.
/// </para>
/// <para>
/// <b>Real per-operation commits, not one shared transaction.</b> The same choice
/// <c>BulkBreakTests</c>' own 1 000-break generative test makes, for the same reason: a shared
/// outer transaction would let a handler's own read-side queries (for example
/// <c>IStockPositionReader</c>'s moving-average read inside a bulk break) see stale data across
/// what look like separate operations, manufacturing an inconsistency no real till - which posts
/// one document, commits, and only then rings up the next - would ever produce. Ten thousand
/// individual <c>BEGIN IMMEDIATE</c>/<c>fsync</c> pairs (CLAUDE.md invariant 9) is the cost of that
/// honesty, and this test accepts it exactly as <c>BulkBreakTests</c> already does at a tenth of
/// the scale.
/// </para>
/// <para>
/// <b>An "operation" is one business event, not one service call.</b> A stock take needs a
/// <c>StartAsync</c>, one or more <c>RecordCountAsync</c> calls and a <c>PostAsync</c> to become a
/// real, ledger-affecting event - that whole cycle is counted as a single one of the 10 000, the
/// same way a shop would describe "we did a stock take" as one thing that happened, not three.
/// </para>
/// </remarks>
public sealed class StockConservationTests
{
    private const int OperationCount = 10_000;
    private const int Seed = 20_260_915;
    private const int PoolSize = 20;

    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task P2_T12_StockBalanceEqualsTheLedgerSumForEveryVariantAfter10000RandomOperations()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        await ConfigureNumberSequencesAsync(fixture);
        var (variantIds, pieceUomId) = await SeedPoolAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);

        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var shiftId = await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

        var openingAt = new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.FromHours(5.5));
        foreach (var variantId in variantIds)
        {
            await PostOpeningStockAsync(fixture, variantId, pieceUomId, 500_000m, 10.00m, openingAt);
        }

        var completeSale = fixture.Resolve<ICompleteSale>();
        var quoteSale = fixture.Resolve<IQuoteSale>();
        var createReturn = fixture.Resolve<ICreateReturn>();
        var goodsReceipt = fixture.Resolve<IGoodsReceiptService>();
        var postAdjustment = fixture.Resolve<IPostAdjustment>();
        var postBulkBreak = fixture.Resolve<IPostBulkBreak>();
        var stockTakes = fixture.Resolve<IStockTakeService>();

        var random = new Random(Seed);
        var clock = openingAt.AddHours(1);
        var openLines = new List<OpenSaleLine>();

        var sales = 0;
        var returns = 0;
        var grns = 0;
        var adjustments = 0;
        var bulkBreaks = 0;
        var stockTakeCycles = 0;

        for (var i = 0; i < OperationCount; i++)
        {
            clock = clock.AddSeconds(2);
            var roll = random.Next(1, 101);

            if (roll <= 60 || (roll <= 80 && openLines.Count == 0))
            {
                await DoSaleAsync();
                sales++;
            }
            else if (roll <= 80)
            {
                await DoReturnAsync();
                returns++;
            }
            else if (roll <= 90)
            {
                await DoGrnAsync();
                grns++;
            }
            else if (roll <= 97)
            {
                await DoAdjustmentAsync();
                adjustments++;
            }
            else if (roll <= 99)
            {
                await DoBulkBreakAsync();
                bulkBreaks++;
            }
            else
            {
                await DoStockTakeCycleAsync();
                stockTakeCycles++;
            }
        }

        (sales + returns + grns + adjustments + bulkBreaks + stockTakeCycles).Should().Be(OperationCount);

        // The consistency check IStockConsistencyCheck already runs at start-up (P1-T07, SAD §3) -
        // sampled here at a size well above the pool, so "sample" covers every variant this test
        // ever touched, not a subset of them.
        var report = await fixture.Resolve<IStockConsistencyCheck>().CheckAsync(sampleSize: PoolSize + 10);

        report.SampledCount.Should().BeGreaterThanOrEqualTo(PoolSize,
            "the sample must cover every variant this test posted a movement against");
        report.Mismatches.Should().BeEmpty(
            $"stock_balance must equal the ledger sum for every variant after {OperationCount} random "
            + $"sales ({sales}), returns ({returns}), GRNs ({grns}), adjustments ({adjustments}), "
            + $"bulk breaks ({bulkBreaks}) and stock take cycles ({stockTakeCycles}) - a mismatch here "
            + "means a code path bypassed StockLedger.PostAsync (CLAUDE.md invariant 3), not a "
            + "rounding artefact");

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

            openLines.Add(new OpenSaleLine(completed.SaleId, saleLineId, variantId, qty));
        }

        async Task DoReturnAsync()
        {
            var index = random.Next(openLines.Count);
            var entry = openLines[index];

            var qtyToReturn = random.Next(1, (int)entry.Remaining + 1);
            var disposition = random.Next(5) == 0 ? ReturnDisposition.Damaged : ReturnDisposition.Sellable;

            await createReturn.CreateAsync(new CreateReturnCommand(
                entry.SaleId,
                userId,
                shiftId,
                clock,
                [new ReturnLineRequest(
                    entry.SaleLineId, Quantity.FromDecimal(qtyToReturn, entry.SaleLineId), disposition, "Random test return")],
                RefundMethod.Cash));

            entry.Remaining -= qtyToReturn;
            if (entry.Remaining <= 0)
            {
                openLines.RemoveAt(index);
            }
        }

        async Task DoGrnAsync()
        {
            var variantId = variantIds[random.Next(variantIds.Count)];
            var qty = random.Next(1, 50);
            var unitCost = Money.FromDecimal(Math.Round(random.Next(100, 10_000) / 100m, 2));

            await goodsReceipt.ReceiveAsync(new CreateGoodsReceiptCommand(
                supplierId, null, null, clock, Money.Zero, null,
                [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, qty, unitCost)]));
        }

        async Task DoAdjustmentAsync()
        {
            var variantId = variantIds[random.Next(variantIds.Count)];

            if (random.Next(2) == 0)
            {
                var delta = random.Next(2) == 0 ? random.Next(1, 20) : -random.Next(1, 20);
                await postAdjustment.AdjustAsync(new AdjustmentCommand(variantId, delta, null, "Random test adjustment", clock));
            }
            else
            {
                var quantity = random.Next(1, 10);
                await postAdjustment.WriteOffDamageAsync(new DamageCommand(variantId, quantity, "Random test damage", clock));
            }
        }

        async Task DoBulkBreakAsync()
        {
            var sourceIndex = random.Next(variantIds.Count);
            int destinationIndex;
            do
            {
                destinationIndex = random.Next(variantIds.Count);
            }
            while (destinationIndex == sourceIndex);

            var sourceQty = random.Next(1, 4);
            var expectedQty = sourceQty * 10;
            var actualQty = expectedQty - random.Next(0, 3);

            await postBulkBreak.PostAsync(new BulkBreakCommand(
                variantIds[sourceIndex], sourceQty, variantIds[destinationIndex], expectedQty, actualQty,
                "Random test break", clock));
        }

        async Task DoStockTakeCycleAsync()
        {
            var started = await stockTakes.StartAsync(new StartStockTakeCommand("ALL", clock));

            var sampleCount = Math.Min(3, variantIds.Count);
            for (var i = 0; i < sampleCount; i++)
            {
                var variantId = variantIds[random.Next(variantIds.Count)];
                var currentQtyRaw = await fixture.CountAsync(
                    "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
                var currentQty = currentQtyRaw / (decimal)Quantity.QtyScale;
                var variance = random.Next(-3, 4);
                var countedQty = Math.Max(0m, currentQty + variance);

                await stockTakes.RecordCountAsync(
                    new RecordStockTakeCountCommand(started.StockTakeId, variantId, countedQty, clock));
            }

            await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, clock.AddSeconds(1)));
        }
    }

    private static async Task ConfigureNumberSequencesAsync(SaleFixture fixture)
    {
        var sequences = fixture.Resolve<INumberSequenceConfiguration>();
        await sequences.ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("STOCK_TAKE", "ST-", "{prefix}{yyyy}-{n:000000}", 1);
    }

    private static async Task<(List<long> VariantIds, long PieceUomId)> SeedPoolAsync(SaleFixture fixture)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;
        var taxClassId = (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

        var variantIds = new List<long>();
        for (var i = 0; i < PoolSize; i++)
        {
            var code = "SC-" + i.ToString("000", CultureInfo.InvariantCulture);
            var productId = await products.CreateAsync(new SaveProductCommand(
                code,
                "Stock conservation stock " + i.ToString(CultureInfo.InvariantCulture),
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
            .CreateAsync(new SaveSupplierCommand("Stock Conservation Supplier", null, null, null, null, null));

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost, DateTimeOffset at)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId, "OPENING", Quantity.FromDecimal(quantity, uomId), Money.FromDecimal(unitCost),
            "OPENING", RefDocId: null, userId, at));
    }

    /// <summary>One completed sale line still available to be returned against, tracked so the
    /// random driver never asks for more back than was actually sold minus what already came
    /// back (AC-06's own rule, exercised here rather than re-implemented).</summary>
    private sealed class OpenSaleLine(long saleId, long saleLineId, long variantId, decimal remaining)
    {
        public long SaleId { get; } = saleId;

        public long SaleLineId { get; } = saleLineId;

        public long VariantId { get; } = variantId;

        public decimal Remaining { get; set; } = remaining;
    }
}
