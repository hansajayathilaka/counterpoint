using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.SeedGenerator;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// <b>AC-19</b> - "Bill numbering is proven gapless across 500 consecutive bills including
/// cancellations."
/// </summary>
/// <remarks>
/// Five hundred real bills through <see cref="ICompleteSale"/>, one in twenty of them cancelled
/// through <see cref="ICancelSale"/> immediately afterwards - the actual document-numbering path
/// (<c>number_sequence</c>, CLAUDE.md invariant 4), not a hand-written sequence. A single example
/// bill would pass and prove nothing: the risk this guards is a gap appearing only after many
/// consecutive allocations, or a cancellation that frees or reuses a number instead of keeping it
/// (CLAUDE.md invariant 4: "a cancelled document keeps its number").
/// </remarks>
public sealed class AC19_GaplessBillNumberingAcrossFiveHundredBillsTests
{
    private const int BillCount = 500;
    private static readonly DateTimeOffset TradingDay =
        new(2026, 9, 6, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_19_FiveHundredConsecutiveBillsIncludingCancellationsProduceAGaplessSeries()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;
        var shiftId = await SeededOpenShiftIdAsync(fixture);

        var completeSale = fixture.Resolve<ICompleteSale>();
        var quoteSale = fixture.Resolve<IQuoteSale>();
        var cancelSale = fixture.Resolve<ICancelSale>();

        var billNumbers = new List<string>(BillCount);
        var cancelledSaleIds = new List<long>();

        for (var i = 0; i < BillCount; i++)
        {
            var soldAt = TradingDay.AddSeconds(i);
            var lines = new List<SaleLineRequest> { new(variantId, 1m) };
            var quote = await quoteSale.QuoteAsync(lines);

            var completed = await completeSale.CompleteAsync(new CompleteSaleCommand(
                userId, shiftId, soldAt, lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));

            billNumbers.Add(completed.BillNo);

            // Every twentieth bill is cancelled on the spot (the only day CancelSaleHandler
            // allows, SRS FR-3.34) - the case CLAUDE.md invariant 4 calls out by name: the number
            // must not come free for a later bill to reuse.
            if ((i + 1) % 20 == 0)
            {
                await cancelSale.CancelAsync(new CancelSaleCommand(
                    completed.SaleId, "AC-19 gapless-numbering sweep", soldAt.AddSeconds(1)));
                cancelledSaleIds.Add(completed.SaleId);
            }
        }

        billNumbers.Should().HaveCount(BillCount);
        billNumbers.Should().OnlyHaveUniqueItems("the same number must never be issued twice");

        var numericParts = billNumbers
            .Select(ParseSequenceNumber)
            .OrderBy(n => n)
            .ToList();

        numericParts.Should().BeEquivalentTo(
            Enumerable.Range(1, BillCount),
            options => options.WithStrictOrdering(),
            "the series must run 1..500 with no gap anywhere - not just be 500 unique numbers");

        // Every cancelled bill still owns the number it was given - status changes, the number
        // never does (CLAUDE.md invariant 4, invariant 5).
        foreach (var saleId in cancelledSaleIds)
        {
            var status = await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + saleId + ";");
            status.Should().Be("CANCELLED");
        }

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'CANCELLED';"))
            .Should().Be(cancelledSaleIds.Count);

        // The row hash chain over all 500 bills - cancelled or not - must still be unbroken:
        // cancelling changes status/cancelled_by/cancelled_at only, none of which the chain
        // covers (CLAUDE.md invariant 6, SaleHashChain's own remarks).
        await AssertSaleHashChainIsIntactAsync(fixture);
    }

    private static long ParseSequenceNumber(string billNo)
    {
        // "INV-2026-000001" - the last hyphen-separated segment is the zero-padded sequence.
        var lastSegment = billNo.Split('-')[^1];
        return long.Parse(lastSegment, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The real hash-chain verification command (P1-T16 deliverable 4), not a hand-rolled check:
    /// every one of the 500 bills, cancelled or not, must still recompute to the <c>row_hash</c>
    /// it was stored with, chained from genesis (CLAUDE.md invariant 6).
    /// </summary>
    private static async Task AssertSaleHashChainIsIntactAsync(SaleFixture fixture)
    {
        var result = await HashChainVerifier.VerifySaleChainAsync(fixture.Resolve<IPosConnectionFactory>());

        result.IsIntact.Should().BeTrue(
            "the sale hash chain must be unbroken across all 500 bills; first break at row {0}",
            result.FirstBrokenRowId);
        result.RowsChecked.Should().Be(BillCount);
    }

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededOpenShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
