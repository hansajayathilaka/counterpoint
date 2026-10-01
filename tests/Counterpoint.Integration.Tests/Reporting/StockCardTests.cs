using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-11, the item stock card (task P3-T06 "Do this" #1), against the hand-worked ledger in
/// <see cref="StockCashDataset"/>. "Done when": the stock card for one item reconstructs its balance history
/// exactly from the ledger - opening plus every movement equals the closing balance, every running balance is
/// the <c>balance_after</c> the ledger recorded, and the closing balance is <c>stock_balance.qty_base</c>.
/// </summary>
/// <remarks>
/// <para>
/// Bolt's ledger, in posting (id) order, with the occurred_at that is deliberately NOT chronological - the Sep 7
/// return R1 (11:00) was posted after the Sep 7 bill B6 (14:10), and the Sep 3 receipt G1 was entered after the
/// Sep 7 trading:
/// </para>
/// <code>
///   OPENING +1000 (Sep 1)            1000
///   SALE    -3  B1 (Sep 6 10:05)      997
///   SALE    -2  B3 (Sep 6 14:20)      995
///   SALE    -1  B6 (Sep 7 14:10)      994
///   RETURN_IN +1 R1 (Sep 7 11:00)     995
///   RETURN_IN +1 R3 (Sep 7 16:00)     996
///   GRN   +100  G1 (Sep 3 10:00)     1096
///   ADJUSTMENT -4 A1 (Sep 8 09:00)   1092
///   DAMAGE     -6 A2 (Sep 8 23:59:59) 1086
///   GRN    +50  G4 (Sep 13 00:00)    1136
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class StockCardTests(StockCashReportFixture fixture)
{
    private static readonly ReportDateRange WholeHistory = ReportDateRange.Custom(new(2026, 9, 1), new(2026, 9, 30));

    private static Quantity Q(decimal value) => Quantity.FromDecimal(value, 0);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private IStockCardQuery Query => fixture.Host.Resolve<IStockCardQuery>();

    [Fact]
    public async Task RPT_11_TheBoltCardOverTheWholeHistoryReconstructsEveryBalanceExactlyFromTheLedger()
    {
        var data = fixture.Data;

        var card = await Query.GetStockCardAsync(data.Sales.BoltVariantId, WholeHistory);

        card.Should().NotBeNull();
        card!.Sku.Should().Be("RPT-BOLT-A");
        card.Description.Should().Be("Bolt");
        card.BaseUomSymbol.Should().Be("pc");
        card.OpeningBalance.Value.Should().Be(0m, "nothing precedes the opening count");

        card.Rows.Select(row => (row.MovementType, row.QtyBase.Value, row.RunningBalance.Value, row.ReferenceNo)).Should().Equal(
        [
            ("OPENING", 1000m, 1000m, string.Empty),
            ("SALE", -3m, 997m, data.Sales.B1.BillNo),
            ("SALE", -2m, 995m, data.Sales.B3.BillNo),
            ("SALE", -1m, 994m, data.Sales.B6.BillNo),
            ("RETURN_IN", 1m, 995m, data.Sales.R1.ReturnNo),
            ("RETURN_IN", 1m, 996m, data.Sales.R3.ReturnNo),
            ("GRN", 100m, 1096m, StockCashDataset.Grn1),
            ("ADJUSTMENT", -4m, 1092m, string.Empty),
            ("DAMAGE", -6m, 1086m, string.Empty),
            ("GRN", 50m, 1136m, StockCashDataset.Grn4),
        ]);

        card.Rows.Select(row => row.UnitCost).Should().Equal(
            M(60.00m), M(60.00m), M(60.00m), M(60.00m), M(60.00m), M(60.00m), M(54.52m), M(59.50m), M(59.50m), M(65.18m));
        card.Rows.Select(row => row.Note).Should().Equal(
            [null, null, null, null, null, null, null, "Stock count correction", "Water damage", null]);

        card.Rows.Should().OnlyContain(row => row.MatchesLedger, "every running balance is the balance_after the ledger recorded");
        card.Rows.Select(row => row.BalanceAfter.Value).Should().Equal(card.Rows.Select(row => row.RunningBalance.Value));
        card.Rows.Select(row => row.MovementId).Should().BeInAscendingOrder("rows are in the ledger's own id order");
        card.Rows.Select(row => row.OccurredAt).Should().NotBeInAscendingOrder(
            "this history posts out of chronological order (R1 after B6, G1 after the Sep 7 returns); ordering by occurred_at would break the chain");

        card.TotalIn.Value.Should().Be(1152m, "1000 + 1 + 1 + 100 + 50");
        card.TotalOut.Value.Should().Be(-16m, "-3 - 2 - 1 - 4 - 6");
        card.ClosingBalance.Value.Should().Be(1136m);
        card.OpeningBalance.Value.Should().Be(0m);
        (card.OpeningBalance.Value + card.TotalIn.Value + card.TotalOut.Value).Should().Be(card.ClosingBalance.Value, "opening + sum(qty_base) == closing");
        card.Reconciles.Should().BeTrue();

        var stored = await fixture.Host.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + data.Sales.BoltVariantId + ";");
        Quantity.FromDecimal(card.ClosingBalance.Value, 0).ToScaled().ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(stored, "the closing balance is stock_balance.qty_base");
    }

    [Fact]
    public async Task RPT_11_ADateRangeTakesItsOpeningBalanceFromTheMovementBeforeItNotASum()
    {
        var data = fixture.Data;

        var card = await Query.GetStockCardAsync(
            data.Sales.BoltVariantId, ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo));

        card!.OpeningBalance.Value.Should().Be(1000m, "the OPENING movement's balance_after, from Sep 1, read and not summed");
        card.Rows.Select(row => (row.MovementType, row.RunningBalance.Value)).Should().Equal(
        [
            ("SALE", 997m), ("SALE", 995m), ("SALE", 994m), ("RETURN_IN", 995m), ("RETURN_IN", 996m),
        ]);
        card.TotalIn.Value.Should().Be(2m);
        card.TotalOut.Value.Should().Be(-6m);
        card.ClosingBalance.Value.Should().Be(996m, "the last in-range movement's balance_after");
        card.Reconciles.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_11_ALaterRangeOpensAtTheBalanceTheLastEarlierMovementLeftAndClosesAtTheLiveBalance()
    {
        var data = fixture.Data;

        var card = await Query.GetStockCardAsync(
            data.Sales.BoltVariantId, ReportDateRange.Custom(new(2026, 9, 8), new(2026, 9, 30)));

        card!.OpeningBalance.Value.Should().Be(1096m, "the receipt G1's balance_after - the movement before the first row in ledger order");
        card.Rows.Select(row => (row.MovementType, row.QtyBase.Value, row.RunningBalance.Value)).Should().Equal(
            [("ADJUSTMENT", -4m, 1092m), ("DAMAGE", -6m, 1086m), ("GRN", 50m, 1136m)]);
        card.ClosingBalance.Value.Should().Be(1136m);
        card.Reconciles.Should().BeTrue();

        (await fixture.Host.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + data.Sales.BoltVariantId + ";"))
            .Should().Be("11360000", "closing equals stock_balance for a range that runs to the end of the ledger");
    }

    [Fact]
    public async Task RPT_11_ABackdatedReceiptIsListedInLedgerOrderAndOpensAtTheBalanceTheLedgerRecordedBeforeIt()
    {
        // G1 was received on Sep 3 but entered after the Sep 7 trading: its ledger position - which is what
        // its balance_after (1096) was built on - is after Bolt's Sep 7 return R3. The report's documented
        // order is the ledger's own id, so the card for Sep 3-5 opens at 996 (the movement before it in the
        // ledger), not at the 1000 a calendar reader might expect. Pinned so a change of ordering rule is deliberate.
        var card = await Query.GetStockCardAsync(
            fixture.Data.Sales.BoltVariantId, ReportDateRange.Custom(new(2026, 9, 3), new(2026, 9, 5)));

        card!.Rows.Should().ContainSingle();
        card.Rows[0].MovementType.Should().Be("GRN");
        card.OpeningBalance.Value.Should().Be(996m);
        card.Rows[0].RunningBalance.Value.Should().Be(1096m);
        card.Rows[0].BalanceAfter.Value.Should().Be(1096m);
        card.Reconciles.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_11_ARangeWhoseRowsAreNotAContiguousRunOfTheLedgerStillReconcilesOnAHealthyLedger()
    {
        // Sep 1-3 holds the OPENING row (lowest id) and the back-dated receipt G1 (entered after the Sep 6-7 trading, so a
        // high id), but none of the five movements posted between them. The ledger is healthy: G1's balance_after (1096)
        // is built on R3's 996, not on OPENING's 1000.
        var card = await Query.GetStockCardAsync(
            fixture.Data.Sales.BoltVariantId, ReportDateRange.Custom(new(2026, 9, 1), new(2026, 9, 3)));

        card!.Rows.Select(row => row.MovementType).Should().Equal("OPENING", "GRN");
        card.Rows.Select(row => row.BalanceAfter.Value).Should().Equal(1000m, 1096m);
        card.Reconciles.Should().BeTrue("a healthy ledger always reconciles, whatever rows the date range happens to select");

        // The card says why the two rows do not run on: G1 follows five movements the range does not show, and was
        // entered after later-dated trading. Its running balance is built on its ledger predecessor (996), not on OPENING.
        card.OpeningBalance.Value.Should().Be(0m);
        card.IsContiguous.Should().BeFalse("movements posted between OPENING and G1 are dated outside the range");
        card.Rows.Select(row => (row.RunningBalance.Value, row.MatchesLedger)).Should().Equal([(1000m, true), (1096m, true)]);
        card.Rows.Select(row => (row.PostedOutOfDateOrder, row.FollowsRowsNotShown)).Should().Equal([(false, false), (true, true)]);
    }

    [Fact]
    public async Task RPT_11_TheWholeHistoryIsContiguousAndFlagsExactlyTheRowsPostedOutOfDateOrder()
    {
        var card = await Query.GetStockCardAsync(fixture.Data.Sales.BoltVariantId, WholeHistory);

        card!.IsContiguous.Should().BeTrue();
        card.Rows.Should().OnlyContain(row => !row.FollowsRowsNotShown);
        card.Rows.Where(row => row.PostedOutOfDateOrder).Select(row => row.MovementType).Should().Equal(
            ["RETURN_IN", "GRN"], "R1 (Sep 7 11:00) was posted after B6 (14:10), and G1 (Sep 3) after R3 (Sep 7)");
        (card.OpeningBalance.Value + card.TotalIn.Value + card.TotalOut.Value).Should().Be(card.ClosingBalance.Value);
    }

    [Fact]
    public async Task RPT_11_ATamperedBalanceAfterStillBreaksTheChainEvenInASparseRange()
    {
        // Scratch database: the append-only trigger stops an UPDATE, but an INSERT is allowed, so build the broken ledger
        // by appending a sale whose recorded balance does not follow from the movement before it.
        await using var host = await Counterpoint.Integration.Tests.Sales.SaleFixture.CreateSignedInAsync();
        var variantId = await Counterpoint.Integration.Tests.Sales.PricedVariantSeeder.SeedAsync(host, "TAMPER-A", 10.00m, 0m);
        var userId = await host.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var seededBalance = await host.CountAsync(
            "SELECT balance_after FROM stock_movement WHERE product_variant_id = " + variantId + " ORDER BY id DESC LIMIT 1;");

        string Insert(long qty, long balanceAfter, string at) =>
            "INSERT INTO stock_movement (product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, ref_doc_id, balance_after, user_id, occurred_at, note) "
            + "VALUES (" + variantId + ", 'ADJUSTMENT', " + qty + ", 100000, 'ADJUSTMENT', NULL, " + balanceAfter + ", " + userId + ", '" + at + "', 'scratch');";

        // A healthy movement, then one whose balance_after is 5.0000 short of what the ledger before it implies.
        await host.ExecuteAsync(Insert(-10_0000, seededBalance - 10_0000, "2026-09-20T10:00:00.000+05:30"));
        await host.ExecuteAsync(Insert(-10_0000, seededBalance - 25_0000, "2026-09-21T10:00:00.000+05:30"));

        var healthy = await host.Resolve<IStockCardQuery>().GetStockCardAsync(
            variantId, ReportDateRange.Custom(new(2026, 9, 20), new(2026, 9, 20)));
        healthy!.Reconciles.Should().BeTrue();

        var broken = await host.Resolve<IStockCardQuery>().GetStockCardAsync(
            variantId, ReportDateRange.Custom(new(2026, 9, 21), new(2026, 9, 21)));
        broken!.Reconciles.Should().BeFalse("the recorded balance does not follow from the movement before it");
        broken.Rows.Should().ContainSingle().Which.MatchesLedger.Should().BeFalse();
        broken.Rows[0].RunningBalance.ToScaled().Should().Be(seededBalance - 20_0000);
        broken.Rows[0].BalanceAfter.ToScaled().Should().Be(seededBalance - 25_0000);

        var whole = await host.Resolve<IStockCardQuery>().GetStockCardAsync(
            variantId, ReportDateRange.Custom(new(2026, 9, 1), new(2026, 9, 30)));
        whole!.Reconciles.Should().BeFalse();
    }

    [Fact]
    public async Task RPT_11_ARangeEndsAtMidnightSoAMovementAtTwentyThreeFiftyNineIsInAndOneAtMidnightIsOut()
    {
        var data = fixture.Data;

        var boltSep8 = await Query.GetStockCardAsync(data.Sales.BoltVariantId, ReportDateRange.Custom(new(2026, 9, 8), new(2026, 9, 8)));
        boltSep8!.Rows.Select(row => row.MovementType).Should().Equal(
            ["ADJUSTMENT", "DAMAGE"], "the 23:59:59 write-off is still Sep 8");

        var drillSep8 = await Query.GetStockCardAsync(data.Sales.DrillVariantId, ReportDateRange.Custom(new(2026, 9, 8), new(2026, 9, 8)));
        drillSep8!.Rows.Select(row => (row.MovementType, row.QtyBase.Value)).Should().Equal([("ADJUSTMENT", 3m)], "the 00:00:00 Sep 9 damage is not Sep 8");
        drillSep8.OpeningBalance.Value.Should().Be(1007m, "the Drill receipt G1's balance_after");
        drillSep8.ClosingBalance.Value.Should().Be(1010m);

        var drillSep9 = await Query.GetStockCardAsync(data.Sales.DrillVariantId, ReportDateRange.Custom(new(2026, 9, 9), new(2026, 9, 9)));
        drillSep9!.Rows.Select(row => (row.MovementType, row.QtyBase.Value, row.RunningBalance.Value)).Should().Equal(
            [("DAMAGE", -2m, 1008m), ("ADJUSTMENT", -1m, 1007m)]);
        drillSep9.OpeningBalance.Value.Should().Be(1010m);
    }

    [Fact]
    public async Task RPT_11_TheDrillCardCarriesEveryMovementTypeIncludingACancellationAndAStockTakeCount()
    {
        var data = fixture.Data;

        var card = await Query.GetStockCardAsync(data.Sales.DrillVariantId, WholeHistory);

        card!.Rows.Select(row => (row.MovementType, row.QtyBase.Value, row.RunningBalance.Value, row.ReferenceNo)).Should().Equal(
        [
            ("OPENING", 1000m, 1000m, string.Empty),
            ("SALE", -1m, 999m, data.Sales.B1.BillNo),
            ("SALE", -1m, 998m, data.Sales.B4Cancelled.BillNo),
            ("SALE", 1m, 999m, data.Sales.B4Cancelled.BillNo),
            ("SALE", -2m, 997m, data.Sales.B5.BillNo),
            ("GRN", 10m, 1007m, StockCashDataset.Grn1),
            ("ADJUSTMENT", 3m, 1010m, string.Empty),
            ("DAMAGE", -2m, 1008m, string.Empty),
            ("ADJUSTMENT", -1m, 1007m, string.Empty),
            ("STOCK_TAKE", -7m, 1000m, StockCashDataset.StockTakeNo),
        ]);

        card.Rows[3].Note.Should().Be("Cancellation of " + data.Sales.B4Cancelled.BillNo, "the reversing movement names the bill it cancels");
        card.Rows[9].Note.Should().Be("Stock take " + StockCashDataset.StockTakeNo);
        card.Rows.Should().OnlyContain(row => row.MatchesLedger);
        card.TotalIn.Value.Should().Be(1014m, "1000 + 1 + 10 + 3");
        card.TotalOut.Value.Should().Be(-14m, "-1 - 1 - 2 - 2 - 1 - 7");
        card.ClosingBalance.Value.Should().Be(1000m);
        card.Reconciles.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_11_TheSameCardComesBackByVariantIdBySkuAndByBarcode()
    {
        var data = fixture.Data;

        var byId = await Query.GetStockCardAsync(data.SkeletonVariantId, WholeHistory);
        var bySku = await Query.GetStockCardBySkuAsync(StockCashDataset.SkeletonSku, WholeHistory);
        var byBarcode = await Query.GetStockCardBySkuAsync(StockCashDataset.SkeletonBarcode, WholeHistory);
        var padded = await Query.GetStockCardBySkuAsync("  " + StockCashDataset.SkeletonBarcode + " ", WholeHistory);

        byId.Should().NotBeNull();
        byId!.Sku.Should().Be(StockCashDataset.SkeletonSku);
        byId.Rows.Should().ContainSingle();
        byId.Rows[0].MovementType.Should().Be("OPENING");
        byId.Rows[0].QtyBase.Value.Should().Be(100m);
        byId.Rows[0].UnitCost.Should().Be(M(9.00m));
        byId.Rows[0].OccurredAt.Should().Be(new DateTimeOffset(2026, 9, 6, 9, 15, 0, SalesReportDataset.ShopOffset));
        byId.ClosingBalance.Value.Should().Be(100m);

        bySku.Should().BeEquivalentTo(byId);
        byBarcode.Should().BeEquivalentTo(byId);
        padded.Should().BeEquivalentTo(byId, "a scanned code's stray spaces are trimmed");
    }

    [Fact]
    public async Task RPT_11_AnUnknownItemGivesAClearNullResultNotAnExceptionOrAnEmptyCard()
    {
        (await Query.GetStockCardAsync(987654321, WholeHistory)).Should().BeNull();
        (await Query.GetStockCardBySkuAsync("NO-SUCH-SKU", WholeHistory)).Should().BeNull();
        (await Query.GetStockCardBySkuAsync("0000000000000", WholeHistory)).Should().BeNull();
        (await Query.GetStockCardBySkuAsync("   ", WholeHistory)).Should().BeNull();
    }

    [Fact]
    public async Task RPT_11_ARangeWithNoMovementOpensAndClosesAtTheBalanceBeforeItAndBeforeAnyMovementAtZero()
    {
        var data = fixture.Data;

        var quiet = await Query.GetStockCardAsync(data.Sales.BoltVariantId, ReportDateRange.Custom(new(2026, 9, 20), new(2026, 9, 25)));
        quiet!.Rows.Should().BeEmpty();
        quiet.OpeningBalance.Value.Should().Be(1136m);
        quiet.ClosingBalance.Value.Should().Be(1136m);
        quiet.TotalIn.Value.Should().Be(0m);
        quiet.TotalOut.Value.Should().Be(0m);
        quiet.Reconciles.Should().BeTrue();

        var before = await Query.GetStockCardAsync(data.Sales.BoltVariantId, ReportDateRange.Custom(new(2026, 8, 1), new(2026, 8, 31)));
        before!.Rows.Should().BeEmpty();
        before.OpeningBalance.Value.Should().Be(0m);
        before.ClosingBalance.Value.Should().Be(0m);
        before.Reconciles.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_11_AFractionalQuantityAtANonRoundCostSurvivesTheCardExactly()
    {
        var card = await Query.GetStockCardAsync(fixture.Data.CordVariantId, WholeHistory);

        card!.Rows.Should().ContainSingle();
        card.Rows[0].QtyBase.Value.Should().Be(12.3456m);
        card.Rows[0].UnitCost.Should().Be(M(7.7777m));
        card.Rows[0].RunningBalance.Value.Should().Be(12.3456m);
        card.ClosingBalance.Value.Should().Be(12.3456m);
        card.Reconciles.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_11_ACancelledSaleShowsItsSaleAndItsReversalAndNetsToNothing()
    {
        var card = await Query.GetStockCardAsync(fixture.Data.HingeVariantId, WholeHistory);

        card!.Rows.Select(row => (row.MovementType, row.QtyBase.Value, row.RunningBalance.Value, row.ReferenceNo)).Should().Equal(
        [
            ("OPENING", 10m, 10m, string.Empty),
            ("SALE", -1m, 9m, "INV-2026-000007"),
            ("SALE", 1m, 10m, "INV-2026-000007"),
        ]);
        card.ClosingBalance.Value.Should().Be(10m);
    }

    [Fact]
    public async Task RPT_11_RunningEveryCardNeverWritesTheStockProjection()
    {
        var data = fixture.Data;
        const string Snapshot = "SELECT group_concat(product_variant_id || ':' || qty_base || ':' || cost_avg || ':' || updated_at, ',') "
            + "FROM (SELECT * FROM stock_balance ORDER BY product_variant_id);";
        var before = await fixture.Host.ScalarAsync(Snapshot);
        var movementsBefore = await fixture.Host.CountAsync("SELECT COUNT(*) FROM stock_movement;");

        foreach (var variant in new[] { data.Sales.BoltVariantId, data.Sales.DrillVariantId, data.Sales.NailVariantId, data.WasherVariantId })
        {
            await Query.GetStockCardAsync(variant, WholeHistory);
        }

        (await fixture.Host.ScalarAsync(Snapshot)).Should().Be(before);
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM stock_movement;")).Should().Be(movementsBefore);
    }

    [Fact]
    public async Task RPT_11_EveryVariantsCardReconcilesAndItsClosingBalanceIsTheStockBalance()
    {
        var variants = new Dictionary<string, long>
        {
            ["Bolt"] = fixture.Data.Sales.BoltVariantId,
            ["Drill"] = fixture.Data.Sales.DrillVariantId,
            ["Nail"] = fixture.Data.Sales.NailVariantId,
            ["Washer"] = fixture.Data.WasherVariantId,
            ["Gasket"] = fixture.Data.GasketVariantId,
            ["Hinge"] = fixture.Data.HingeVariantId,
            ["Cord"] = fixture.Data.CordVariantId,
            ["Skeleton"] = fixture.Data.SkeletonVariantId,
        };

        foreach (var (name, id) in variants)
        {
            var card = await Query.GetStockCardAsync(id, WholeHistory);
            var stored = await fixture.Host.CountAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + id + ";");

            card!.Reconciles.Should().BeTrue("{0}'s ledger chain is unbroken", name);
            card.ClosingBalance.ToScaled().Should().Be(stored, "{0}'s card closes at stock_balance.qty_base", name);
        }
    }
}
