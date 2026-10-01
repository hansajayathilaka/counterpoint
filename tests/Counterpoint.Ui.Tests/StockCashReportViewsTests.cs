using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Ui.Tests.Support;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Reports;
using Counterpoint.Ui.Views;
using Counterpoint.Ui.Views.Reports;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T06's eleven Stock, Tax and Cash report screens and their shared table, built for real in a headless Avalonia
/// window: each view constructs and lays out, every input on it has a visible label (SRS UI-14, AC-22), the bound
/// figures reach the screen, the table caps at 1 000 rows with a "narrow the filter" note, a refusal or a failed read is
/// a plain sentence (and the failure is logged), and the section host and the nav rail show the right screens to the
/// right role. The figures come from <see cref="StockCashStubs"/>; the real ones are proved in the Integration tests.
/// </summary>
public sealed class StockCashReportViewsTests
{
    private static readonly TimeProvider Clock = new StockCashClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

    private const string ReadFailed =
        "could not be read from the shop's database. Wait a moment and press Run again. If it keeps failing, contact support - the details are in the log.";

    private const string NotProduced =
        "could not be produced. Nothing has been changed. Press Run again; if it keeps failing, contact support - the details are in the log.";

    /// <summary>Section name, the view that shows it, and whether a cashier is offered it.</summary>
    public static readonly TheoryData<string, string, bool> Sections = new()
    {
        { "Stock on hand", "StockOnHandView", true },
        { "Reorder list", "ReorderListView", true },
        { "Stock valuation", "StockValuationView", false },
        { "Stock card", "StockCardView", false },
        { "Slow-moving stock", "SlowMovingStockView", false },
        { "Fast-moving items", "FastMovingView", false },
        { "Damage and adjustments", "DamageAdjustmentView", false },
        { "Supplier purchases", "SupplierPurchasesView", false },
        { "Tax", "TaxReportView", false },
        { "Tender reconciliation", "TenderReconciliationView", false },
        { "Shift variance", "ShiftVarianceView", false },
    };

    private static readonly string[] AllScreenViewNames =
    [
        "SalesSummaryReportView", "SalesByItemReportView", "ProfitReportView", "ReturnsReportView",
        "StockOnHandView", "ReorderListView", "StockValuationView", "StockCardView", "SlowMovingStockView", "FastMovingView",
        "DamageAdjustmentView", "SupplierPurchasesView", "TaxReportView", "TenderReconciliationView", "ShiftVarianceView",
    ];

    // ---- Views: construct, lay out, every input labelled -----------------------------------------

    [AvaloniaFact]
    public void UI_14_EveryNewReportViewAndTheSharedTableConstructLayOutAndHaveNoUnlabelledInputWithoutADataContext()
    {
        Control[] views =
        [
            new StockOnHandView(),
            new ReorderListView(),
            new StockValuationView(),
            new StockCardView(),
            new SlowMovingStockView(),
            new FastMovingView(),
            new DamageAdjustmentView(),
            new SupplierPurchasesView(),
            new TaxReportView(),
            new TenderReconciliationView(),
            new ShiftVarianceView(),
            new ReportTableView(),
        ];

        foreach (var view in views)
        {
            ViewLabelInspector.FindInputsWithoutVisibleLabel(view)
                .Should().BeEmpty("{0} must label every input it carries (AC-22)", view.GetType().Name);
        }
    }

    [Fact]
    public async Task RPT_11_TheStockCardScreenSaysWhyABackDatedRowDoesNotRunOnFromTheRowAboveItAndDoesNotCallItBroken()
    {
        var world = new World(Role.Owner);
        world.StockCardQuery.Sparse = true;
        world.StockCard.SkuText = "RPT-BOLT-A";

        await world.StockCard.RunCommand.ExecuteAsync(null);

        world.StockCard.ReconcilesWarning.Should().BeFalse("the ledger is healthy");
        world.StockCard.ReconcilesText.Should().StartWith("Reconciles: every row follows on from the movement posted before it.");
        world.StockCard.ReconcilesText.Should().NotContain("support");
        world.StockCard.Movements.Rows[0].Cells[7].Text.Should().BeEmpty("a row posted in date order carries only its own note");
        world.StockCard.Movements.Rows[1].Cells[7].Text.Should().Be(
            "Stock count correction - Back-dated: entered after later-dated movements, some outside this range.");
    }

    [AvaloniaFact]
    public async Task UI_14_EveryNewReportViewHasNoUnlabelledInputOnceItIsBoundToARunningViewModel()
    {
        var world = new World(Role.Owner);
        await world.RunAllAsync();
        world.StockCard.SkuText = "RPT-BOLT-A";
        await world.StockCard.RunCommand.ExecuteAsync(null);

        (Control View, object Context)[] bound =
        [
            (new StockOnHandView(), world.StockOnHand),
            (new ReorderListView(), world.Reorder),
            (new StockValuationView(), world.Valuation),
            (new StockCardView(), world.StockCard),
            (new SlowMovingStockView(), world.SlowMoving),
            (new FastMovingView(), world.FastMoving),
            (new DamageAdjustmentView(), world.Damage),
            (new SupplierPurchasesView(), world.Purchases),
            (new TaxReportView(), world.Tax),
            (new TenderReconciliationView(), world.Tender),
            (new ShiftVarianceView(), world.Variance),
        ];

        foreach (var (view, context) in bound)
        {
            view.DataContext = context;

            ViewLabelInspector.FindInputsWithoutVisibleLabel(view)
                .Should().BeEmpty("{0} must label every input, rows and all (AC-22)", view.GetType().Name);
        }

        var table = new ReportTableView { DataContext = world.Tax.Rates };
        ViewLabelInspector.FindInputsWithoutVisibleLabel(table).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task RPT_19_TheTaxViewShowsTheHeaderTheRatesWithTheExemptAndUnknownRowsAndTheReconciliationNote()
    {
        var world = new World(Role.Owner);
        await world.Tax.RunCommand.ExecuteAsync(null);

        var view = new TaxReportView { DataContext = world.Tax };
        var window = new Window { Content = view, Width = 1300, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = TextsOf(view);

        texts.Should().Contain("Kandy Hardware");
        texts.Should().Contain("VAT registration number: TIN-204-118-77");
        texts.Should().Contain("Prices exclude VAT; it is added on top. Taxable values below are the prices charged.");
        texts.Should().Contain("Exempt / zero-rated (0%)");
        texts.Should().Contain("VAT 10%");
        texts.Should().Contain("Rate unknown (unlinked returns)");
        texts.Should().Contain("1,294.00").And.Contain("129.40").And.Contain("335.50").And.Contain("958.50");
        texts.Should().Contain("85.85", "the net tax due");
        texts.Should().Contain("VAT on sales ties to the bill lines and bill headers for this period.");
    }

    [AvaloniaFact]
    public async Task RPT_21_TheTenderViewShowsEachTendersTieOutAndTheNotZdShiftWithItsReason()
    {
        var world = new World(Role.Owner);
        await world.Tender.RunCommand.ExecuteAsync(null);

        var view = new TenderReconciliationView { DataContext = world.Tender };
        var window = new Window { Content = view, Width = 1300, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = TextsOf(view);

        texts.Should().Contain("Bank transfer").And.Contain("Cash");
        texts.Should().Contain("340.95").And.Contain("990.95").And.Contain("1,360.00").And.Contain("369.05");
        texts.Should().Contain("SH-000002").And.Contain("Open");
        texts.Should().Contain("Not Z'd yet - this shift is still open.");
        texts.Should().Contain("Does not tie out yet: some trading in this range is not on a closed shift's Z report (see below).");
    }

    // ---- The shared table ------------------------------------------------------------------------

    [Fact]
    public void RPT_08_ATableStopsAtOneThousandRowsAndSaysHowManyWereLeftOut()
    {
        var table = new ReportTableViewModel("Stock", "Nothing.", new ReportColumn("SKU", 100), new ReportColumn("Qty", 80, IsNumeric: true));

        table.IsEmpty.Should().BeTrue();
        table.HasHiddenRows.Should().BeFalse();

        for (var i = 0; i < 1005; i++)
        {
            table.Add("SKU-" + i, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        table.Rows.Should().HaveCount(1000);
        ReportTableViewModel.RowLimit.Should().Be(1000);
        table.HiddenRowCount.Should().Be(5);
        table.HasHiddenRows.Should().BeTrue();
        table.HiddenRowsText.Should().Be("5 more rows are not shown. Narrow the filter or the dates to see them.");
        table.HasRows.Should().BeTrue();
        table.IsEmpty.Should().BeFalse();

        table.Clear();

        table.Rows.Should().BeEmpty();
        table.HiddenRowCount.Should().Be(0);
        table.HasHiddenRows.Should().BeFalse();
        table.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void RPT_08_ABoldAWarningAndAnOrdinaryRowKeepTheirFlagsAndARowOfTheWrongWidthIsRefused()
    {
        var table = new ReportTableViewModel("", "Nothing.", new ReportColumn("A", 100), new ReportColumn("B", 80, IsNumeric: true));

        table.Add("a", "1");
        table.AddBold("Total", "9");
        table.AddWarning([1], "w", "5");

        table.HasTitle.Should().BeFalse();
        table.Headers.Select(header => header.Text).Should().Equal("A", "B");
        table.Headers.Should().OnlyContain(header => header.IsBold);
        table.Rows[0].Cells.Select(cell => (cell.IsBold, cell.IsWarning)).Should().Equal([(false, false), (false, false)]);
        table.Rows[1].Cells.Should().OnlyContain(cell => cell.IsBold && !cell.IsWarning);
        table.Rows[2].Cells.Select(cell => cell.IsWarning).Should().Equal(false, true);
        table.Rows[0].Cells[1].IsNumeric.Should().BeTrue();
        table.Rows[0].Cells[1].Width.Should().Be(80);

        var act = () => table.Add("only one");
        act.Should().Throw<ArgumentException>().WithMessage("A row needs 2 cells, got 1.*");
    }

    [AvaloniaFact]
    public void RPT_08_TheTableViewShowsTheNarrowTheFilterNoteOnlyWhenRowsWereLeftOutAndTheEmptySentenceOnlyWhenThereAreNone()
    {
        var table = new ReportTableViewModel("Stock", "Nothing on the shelf.", new ReportColumn("SKU", 100));
        var view = new ReportTableView { DataContext = table };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        VisibleTextsOf(view).Should().Contain("Nothing on the shelf.", "an empty table says so");
        VisibleTextsOf(view).Should().NotContain(text => text.Contains("Narrow the filter", StringComparison.Ordinal));

        for (var i = 0; i < 1003; i++)
        {
            table.Add("SKU-" + i);
        }

        Dispatcher.UIThread.RunJobs();

        var visible = VisibleTextsOf(view);
        visible.Should().Contain("3 more rows are not shown. Narrow the filter or the dates to see them.");
        visible.Should().NotContain("Nothing on the shelf.");
        visible.Should().Contain("SKU-0");
        visible.Should().NotContain("SKU-1000", "the thousand and first row is left out");
    }

    [Fact]
    public async Task RPT_08_AScreenGivenMoreThanAThousandLinesShowsAThousandAndCountsAllOfThem()
    {
        var onHand = new StubStockOnHandQuery { LineCount = 1200 };
        var screen = new StockOnHandViewModel(onHand, new StubReportFilterLookup());

        await screen.RunCommand.ExecuteAsync(null);

        screen.Lines.Rows.Should().HaveCount(1000);
        screen.Lines.HiddenRowCount.Should().Be(200);
        screen.ItemCountText.Should().Be("1200", "the count is of the report, not of the rows shown");
    }

    // ---- The screens' own behaviour --------------------------------------------------------------

    [Fact]
    public async Task RPT_19_TheTaxScreenFormatsTheHeaderRowsAndTotalsAndNamesTheUnlinkedAndExemptRows()
    {
        var world = new World(Role.Owner);

        await world.Tax.RunCommand.ExecuteAsync(null);

        world.Tax.ShopText.Should().Be("Kandy Hardware");
        world.Tax.RegistrationText.Should().Be("VAT registration number: TIN-204-118-77");
        world.Tax.BasisText.Should().Be("Prices exclude VAT; it is added on top. Taxable values below are the prices charged.");
        world.Tax.TotalSalesTaxableText.Should().Be("1,594.00");
        world.Tax.TotalSalesTaxText.Should().Be("129.40");
        world.Tax.TotalReturnsTaxText.Should().Be("43.55");
        world.Tax.NetTaxText.Should().Be("85.85");
        world.Tax.ReconciliationWarning.Should().BeFalse();
        world.Tax.ReconciliationText.Should().Be("VAT on sales ties to the bill lines and bill headers for this period.");
        world.Tax.HasStatus.Should().BeFalse();

        world.Tax.Rates.Rows.Select(row => row.Cells[0].Text).Should().Equal(
            "Exempt / zero-rated (0%)", "VAT 10%", "Rate unknown (unlinked returns)", "Total");
        world.Tax.Rates.Rows[1].Cells.Select(cell => cell.Text).Should().Equal(
            "VAT 10%", "1,294.00", "129.40", "335.50", "33.55", "958.50", "95.85");
        world.Tax.Rates.Rows[2].Cells.Select(cell => cell.Text).Should().Equal(
            "Rate unknown (unlinked returns)", "0.00", "0.00", "100.00", "10.00", "-100.00", "-10.00");
        world.Tax.Rates.Rows[3].Cells.Should().OnlyContain(cell => cell.IsBold);
        world.Tax.Rates.Rows[3].Cells.Select(cell => cell.Text).Should().Equal(
            "Total", "1,594.00", "129.40", "435.50", "43.55", "1,158.50", "85.85");
    }

    [Fact]
    public async Task RPT_19_ATaxReportThatDoesNotReconcileTellsTheOwnerNotToFileIt()
    {
        var world = new World(Role.Owner);
        world.TaxQuery.Reconciled = false;

        await world.Tax.RunCommand.ExecuteAsync(null);

        world.Tax.ReconciliationWarning.Should().BeTrue();
        world.Tax.ReconciliationText.Should().Be(
            "VAT on sales does not tie to the bill lines (129.00) or bill headers (129.40). Do not file this report; ask for support.");
    }

    [Fact]
    public async Task RPT_19_TheTaxScreenReadsTheShopsOwnWordsSoNoRegimeIsBakedIn()
    {
        var world = new World(Role.Owner);
        world.TaxQuery.Label = "GST";
        world.TaxQuery.ShopName = "Galle Tools";
        world.TaxQuery.PricesIncludeTax = true;

        await world.Tax.RunCommand.ExecuteAsync(null);

        world.Tax.ShopText.Should().Be("Galle Tools");
        world.Tax.RegistrationText.Should().Be("GST registration number: TIN-204-118-77");
        world.Tax.BasisText.Should().Be("Prices include GST. Taxable values below exclude it.");
        world.Tax.Rates.Rows[1].Cells[0].Text.Should().Be("GST 10%");

        world.TaxQuery.Label = " ";
        world.TaxQuery.ShopName = string.Empty;
        world.TaxQuery.Registration = string.Empty;
        await world.Tax.RunCommand.ExecuteAsync(null);

        world.Tax.ShopText.Should().Be("(shop name not set)");
        world.Tax.RegistrationText.Should().Be("Tax registration number: not set");
        world.Tax.Rates.Rows[1].Cells[0].Text.Should().Be("Tax 10%");
    }

    [Fact]
    public async Task RPT_19_ATaxRangeWithNoTradingSaysSoAndHasNoTotalRow()
    {
        var world = new World(Role.Owner);
        world.TaxQuery.Empty = true;

        await world.Tax.RunCommand.ExecuteAsync(null);

        world.Tax.Rates.Rows.Should().BeEmpty();
        world.Tax.Status.Should().Be("No sales or returns in this range.");
    }

    [Fact]
    public async Task RPT_21_TheTenderScreenFlagsADifferenceAndNamesWhyAndSaysTiesOutOnlyWhenItDoes()
    {
        var world = new World(Role.Owner);

        await world.Tender.RunCommand.ExecuteAsync(null);

        world.Tender.ZNetTotalText.Should().Be("750.00");
        world.Tender.RangeNetTotalText.Should().Be("1,090.95");
        world.Tender.DifferenceText.Should().Be("340.95");
        world.Tender.TieOutWarning.Should().BeTrue();
        world.Tender.TieOutText.Should().Be("Does not tie out yet: some trading in this range is not on a closed shift's Z report (see below).");

        world.Tender.ByTender.Rows.Select(row => row.Cells[0].Text).Should().Equal("Bank transfer", "Cash", "Total");
        world.Tender.ByTender.Rows[0].Cells.Any(cell => cell.IsWarning).Should().BeFalse("a zero difference is not flagged");
        world.Tender.ByTender.Rows[1].Cells[7].IsWarning.Should().BeTrue("a difference that should be zero is flagged");
        world.Tender.ByTender.Rows[1].Cells[7].Text.Should().Be("340.95");
        world.Tender.ByTender.Rows[2].Cells.Should().OnlyContain(cell => cell.IsBold);

        world.Tender.Shifts.Rows.Should().ContainSingle();
        world.Tender.Shifts.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "SH-000001", "2026-09-06", "Cash", "650.00", "0.00", "650.00");

        world.Tender.NotZd.Rows.Should().ContainSingle();
        world.Tender.NotZd.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "SH-000002", "Open", "2026-09-07", "2", "3", "Not Z'd yet - this shift is still open.");
        world.Tender.NotZd.Rows[0].Cells[0].IsWarning.Should().BeTrue();

        world.TenderQuery.OpenShiftListed = false;
        await world.Tender.RunCommand.ExecuteAsync(null);
        world.Tender.TieOutText.Should().Be(
            "Does not tie out: a tender differs between the Z reports and the payments. Ask for support before relying on either.");

        world.TenderQuery.TiedOut = true;
        await world.Tender.RunCommand.ExecuteAsync(null);
        world.Tender.TieOutWarning.Should().BeFalse();
        world.Tender.TieOutText.Should().Be("Ties out: the closed shifts' Z tenders equal the period's payments, tender by tender.");
        world.Tender.DifferenceText.Should().Be("0.00");
        world.Tender.NotZd.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task RPT_21_TheTenderScreenExplainsADateBoundaryDifferenceInsteadOfSendingTheOwnerToSupport()
    {
        var world = new World(Role.Owner);
        world.TenderQuery.DateBoundaryListed = true;

        await world.Tender.RunCommand.ExecuteAsync(null);

        world.Tender.TieOutWarning.Should().BeTrue();
        world.Tender.TieOutText.Should().Be(
            "Does not tie out, only because of dates: a shift's Z report is dated inside this range but part of its trading is dated "
            + "outside it (see below). Widen the range to take in the whole shift.");
        world.Tender.TieOutText.Should().NotContain("support");

        world.Tender.NotZd.Rows.Should().ContainSingle();
        world.Tender.NotZd.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "SH-000001",
            "Closed",
            "2026-09-06",
            "2",
            "3",
            "This shift's Z report is dated 2026-09-06 but it also holds trading dated 2026-09-07, outside this range.");
    }

    [Fact]
    public async Task RPT_21_TheTenderScreenStillSaysSomethingIsUnexplainedWhenTheDifferenceIsLargerThanTheListedShiftsAccountFor()
    {
        var world = new World(Role.Owner);

        // The open shift accounts for 300.00 of the 340.95 difference; 40.95 is unexplained.
        world.TenderQuery.ListedEffect = 300.00m;
        await world.Tender.RunCommand.ExecuteAsync(null);
        world.Tender.TieOutText.Should().Be(
            "Does not tie out: a tender differs between the Z reports and the payments. Ask for support before relying on either.");
        world.Tender.NotZd.Rows.Should().ContainSingle("the listed shift is still shown");

        // Same for a date-boundary shift that explains only part of it.
        world.TenderQuery.DateBoundaryListed = true;
        await world.Tender.RunCommand.ExecuteAsync(null);
        world.Tender.TieOutText.Should().Contain("Ask for support");

        // Once the listed shift accounts for the whole difference the message changes.
        world.TenderQuery.ListedEffect = 340.95m;
        await world.Tender.RunCommand.ExecuteAsync(null);
        world.Tender.TieOutText.Should().NotContain("support");
    }

    [Theory]
    [InlineData(VarianceTrend.Improving, "Improving: the average swing fell from 610.00 in the earlier shifts to 120.00 in the later ones.")]
    [InlineData(VarianceTrend.Worsening, "Worsening: the average swing rose from 610.00 in the earlier shifts to 120.00 in the later ones.")]
    [InlineData(VarianceTrend.Steady, "Steady: the average swing is the same in the earlier and later shifts (610.00).")]
    [InlineData(VarianceTrend.NotEnoughData, "Not enough closed shifts in this range (at least four) to show a trend.")]
    public async Task RPT_21_TheVarianceScreenStatesTheTrendAndOnlyClaimsOneWithFourShifts(VarianceTrend trend, string expected)
    {
        var world = new World(Role.Owner);
        world.VarianceQuery.Trend = trend;

        await world.Variance.RunCommand.ExecuteAsync(null);

        world.Variance.TrendText.Should().Be(expected);
        world.Variance.ShiftCountText.Should().Be("2");
        world.Variance.NetVarianceText.Should().Be("490.00");
        world.Variance.TotalOverText.Should().Be("610.00");
        world.Variance.TotalShortText.Should().Be("-120.00");
        world.Variance.MeanAbsoluteText.Should().Be("365.00");
        world.Variance.ThresholdText.Should().Be("500.00 - 1 shift(s) above it");

        world.Variance.Shifts.Rows.Select(row => row.Cells[0].Text).Should().Equal("SH-000001", "SH-000002");
        world.Variance.Shifts.Rows[0].Cells[6].IsWarning.Should().BeTrue("+610.00 is above the note threshold");
        world.Variance.Shifts.Rows[1].Cells[6].IsWarning.Should().BeFalse();
        world.Variance.Shifts.Rows[1].Cells.Select(cell => cell.Text).Should().Equal(
            "SH-000002", "2026-09-07", "Shop Owner", "Shop Owner", "880.00", "1,000.00", "-120.00", "490.00", string.Empty);
    }

    [Fact]
    public async Task RPT_11_TheStockCardScreenTrimsTheKeyFlagsARowThatDoesNotMatchTheLedgerAndAnswersAnUnknownItem()
    {
        var world = new World(Role.Owner);

        await world.StockCard.RunCommand.ExecuteAsync(null);
        world.StockCardQuery.Calls.Should().Be(0, "nothing typed, nothing asked");
        world.StockCard.Status.Should().Be("Type a SKU or scan a barcode first.");

        world.StockCard.SkuText = "  RPT-BOLT-A ";
        await world.StockCard.RunCommand.ExecuteAsync(null);

        world.StockCardQuery.LastKey.Should().Be("RPT-BOLT-A");
        world.StockCard.ItemText.Should().Be("RPT-BOLT-A - Bolt (pc)");
        world.StockCard.OpeningText.Should().Be("1000 pc");
        world.StockCard.TotalInText.Should().Be("0 pc");
        world.StockCard.TotalOutText.Should().Be("-7 pc");
        world.StockCard.ClosingText.Should().Be("993 pc");
        world.StockCard.ReconcilesWarning.Should().BeFalse();
        world.StockCard.ReconcilesText.Should().Be("Reconciles: opening balance plus every movement equals the closing balance, and every row matches the ledger.");
        world.StockCard.Status.Should().BeEmpty();
        world.StockCard.Movements.Rows.Select(row => string.Join(" | ", row.Cells.Select(cell => cell.Text))).Should().Equal(
            "2026-09-06 10:05 | Sale | INV-2026-000001 | -3 | 997 | 997 | 60.00 | ",
            "2026-09-06 11:05 | Adjustment | Adjustment | -4 | 993 | 993 | 59.50 | Stock count correction");

        world.StockCardQuery.Broken = true;
        await world.StockCard.RunCommand.ExecuteAsync(null);

        world.StockCard.ReconcilesWarning.Should().BeTrue();
        world.StockCard.ReconcilesText.Should().StartWith("Does not reconcile: the ledger chain for this item is broken");
        world.StockCard.Movements.Rows[1].Cells[4].IsWarning.Should().BeTrue("the carried balance differs from the ledger's");
        world.StockCard.Movements.Rows[1].Cells[5].IsWarning.Should().BeTrue();
        world.StockCard.Movements.Rows[0].Cells[4].IsWarning.Should().BeFalse();

        world.StockCardQuery.Unknown = true;
        await world.StockCard.RunCommand.ExecuteAsync(null);

        world.StockCard.Status.Should().Be("No item has that SKU or barcode.");
        world.StockCard.Movements.Rows.Should().BeEmpty();
        world.StockCard.ItemText.Should().Be("-");
        world.StockCard.OpeningText.Should().Be("-");
        world.StockCard.ReconcilesText.Should().BeEmpty();
    }

    [Fact]
    public async Task RPT_15_TheDamageScreenNamesEachKindInPlainWordsAndShowsLossGainAndNet()
    {
        var world = new World(Role.Owner);

        await world.Damage.RunCommand.ExecuteAsync(null);

        world.Damage.Rows.Rows.Select(row => row.Cells[0].Text).Should().Equal("Damage write-off", "Damaged return", "Adjustment");
        world.Damage.Rows.Rows[0].Cells.Select(cell => cell.Text).Should().Equal("Damage write-off", "Water damage", "2", "-8", "-656.00");
        world.Damage.NetValueText.Should().Be("-357.50");
        world.Damage.TotalLossText.Should().Be("806.00");
        world.Damage.TotalGainText.Should().Be("448.50");
    }

    [Fact]
    public async Task RPT_16_ThePurchasesScreenPassesTheChosenSupplierMakesRepeatedNamesUniqueAndLoadsTheListOnce()
    {
        var world = new World(Role.Owner);

        await world.Purchases.RunCommand.ExecuteAsync(null);
        world.PurchasesQuery.LastSupplier.Should().BeNull("(All) is no filter");
        world.Purchases.SupplierLabels.Should().Equal("(All)", "Acme Fasteners", "Zenith Tools", "Acme Fasteners (#3)");

        world.Purchases.SelectedSupplierLabel = "Zenith Tools";
        await world.Purchases.RunCommand.ExecuteAsync(null);
        world.PurchasesQuery.LastSupplier.Should().Be(2);

        world.Purchases.SelectedSupplierLabel = "Acme Fasteners (#3)";
        await world.Purchases.RunCommand.ExecuteAsync(null);
        world.PurchasesQuery.LastSupplier.Should().Be(3, "a repeated name is told apart by id");

        world.Filters.SupplierCalls.Should().Be(1, "the list is filled on the first run only");
        world.Purchases.TotalValueText.Should().Be("9,707.50");
        world.Purchases.TotalTaxText.Should().Be("644.85");
        world.Purchases.TotalPurchasesText.Should().Be("10,352.35");
        world.Purchases.ByItem.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "RPT-BOLT-A", "Bolt", "150", "8,711.00", "58.07", "54.52", "65.18", "10.66", "19.6%");
        world.Purchases.CostMovement.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "RPT-BOLT-A", "2026-09-06 10:05", "Acme Fasteners", "GRN-2026-000001", "54.52");
    }

    [Fact]
    public async Task RPT_09_TheValuationScreenPassesTheChosenCategoryAndStatesThatItIsAsAtNow()
    {
        var world = new World(Role.Owner);

        await world.Valuation.RunCommand.ExecuteAsync(null);
        world.ValuationQuery.LastFilter!.CategoryId.Should().BeNull();
        world.Valuation.CategoryLabels.Should().Equal("(All)", "Fasteners", "Fasteners / Machine screws", "Tools");

        world.Valuation.SelectedCategoryLabel = "Fasteners / Machine screws";
        await world.Valuation.RunCommand.ExecuteAsync(null);

        world.ValuationQuery.LastFilter!.CategoryId.Should().Be(11);
        world.Valuation.AsAtText.Should().StartWith("As at now (2026-09-07 12:00)").And.Contain("A past date cannot be valued.");
        world.Valuation.TotalCostText.Should().Be("149,500.00");
        world.Valuation.TotalPriceText.Should().Be("250,000.00");
        world.Valuation.Lines.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "RPT-DRILL-A", "Drill", "Tools", "1000 pc", "149.50", "149,500.00", "250.00", "250,000.00");
        world.Filters.CategoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task RPT_12_TheSlowMovingScreenValidatesTheDaysAndMeasuresIdleTimeFromTheLastSaleOrTheFirstMovement()
    {
        var world = new World(Role.Owner);

        foreach (var bad in new[] { "abc", "0", "-5", "3651", "", "9.5" })
        {
            world.SlowMoving.DaysText = bad;
            await world.SlowMoving.RunCommand.ExecuteAsync(null);

            world.SlowMoving.DaysValidation.Should().Be("Type a whole number of days from 1 to 3650.", "'{0}' is not a number of days", bad);
        }

        world.SlowMovingQuery.Calls.Should().Be(0, "a bad number never reaches the query");

        world.SlowMoving.DaysText = "90";
        await world.SlowMoving.RunCommand.ExecuteAsync(null);

        world.SlowMoving.DaysValidation.Should().BeEmpty();
        world.SlowMovingQuery.LastFilter!.OlderThan.Should().Be(new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero), "now less 90 days");
        world.SlowMoving.ItemCountText.Should().Be("2");
        world.SlowMoving.TotalValueText.Should().Be("149,685.00");
        world.SlowMoving.Lines.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "RPT-DRILL-A", "Drill", "Tools", "1000 pc", "2026-06-01", "98", "149.50", "149,500.00");
        world.SlowMoving.Lines.Rows[1].Cells.Select(cell => cell.Text).Should().Equal(
            "EXT-GASKET-A", "Gasket", string.Empty, "20 pc", "Never (since 2026-05-22)", "108", "9.25", "185.00");
    }

    [Fact]
    public async Task RPT_13_TheFastMovingScreenValidatesTopNAndShowsBothRankings()
    {
        var world = new World(Role.Owner);

        foreach (var bad in new[] { "abc", "0", "501", "-1", "" })
        {
            world.FastMoving.TopNText = bad;
            await world.FastMoving.RunCommand.ExecuteAsync(null);

            world.FastMoving.TopNValidation.Should().Be("Type a whole number from 1 to 500.", "'{0}'", bad);
        }

        world.FastMovingQuery.Calls.Should().Be(0);

        world.FastMoving.TopNText = "5";
        await world.FastMoving.RunCommand.ExecuteAsync(null);

        world.FastMovingQuery.LastTopN.Should().Be(5);
        world.FastMoving.TopNValidation.Should().BeEmpty();
        world.FastMoving.ByUnits.Rows[0].Cells.Select(cell => cell.Text).Should().Equal("1", "RPT-NAIL-A", "Nail", "29 pc", "250.00");
        world.FastMoving.ByValue.Rows[0].Cells.Select(cell => cell.Text).Should().Equal("1", "RPT-DRILL-A", "Drill", "487.50", "2 pc");
    }

    [Fact]
    public async Task RPT_10_TheReorderScreenGroupsBySupplierPutsNoSupplierLinkedLastAndPassesTheFilterIds()
    {
        var world = new World(Role.Cashier);

        await world.Reorder.RunCommand.ExecuteAsync(null);

        world.Reorder.Lines.Rows.Select(row => row.Cells[0].Text).Should().Equal(
            "Acme Fasteners (1)", "RPT-BOLT", "No supplier linked (1)", "EXT-HINGE");
        world.Reorder.Lines.Rows[0].Cells.Should().OnlyContain(cell => cell.IsBold, "a group heading is bold");
        world.Reorder.Lines.Rows[1].Cells.Select(cell => cell.Text).Should().Equal(
            "RPT-BOLT", "Bolt", "Fasteners", "1136 pc", "1200 pc", "500 pc");
        world.Reorder.ProductCountText.Should().Be("2");
        world.ReorderQuery.LastFilter.Should().Be(new Counterpoint.Application.Inventory.ReorderListFilter());

        world.Reorder.SelectedSupplierLabel = "Acme Fasteners";
        world.Reorder.SelectedCategoryLabel = "Tools";
        await world.Reorder.RunCommand.ExecuteAsync(null);

        world.ReorderQuery.LastFilter.Should().Be(new Counterpoint.Application.Inventory.ReorderListFilter(1, 12));

        world.Reorder.SelectedSupplierLabel = "Not a supplier";
        await world.Reorder.RunCommand.ExecuteAsync(null);
        world.ReorderQuery.LastFilter!.SupplierId.Should().BeNull("an unknown label is no filter");
    }

    [Fact]
    public async Task RPT_08_TheStockOnHandScreenShowsAlternateUnitsPassesEveryFilterAndLoadsTheListsOnce()
    {
        var world = new World(Role.Cashier);

        await world.StockOnHand.RunCommand.ExecuteAsync(null);

        world.StockOnHand.Lines.Rows[0].Cells.Select(cell => cell.Text).Should().Equal(
            "SKU-1", "Nail", "Hardware", "Acme", "A1", "971 pc", "80.9167 box", "50");
        world.StockOnHand.Lines.Rows[1].Cells[6].Text.Should().BeEmpty();
        world.StockOnHand.Lines.Rows[1].Cells[7].Text.Should().BeEmpty("a reorder level of zero is not shown");
        world.StockOnHand.ItemCountText.Should().Be("2");

        world.StockOnHand.SelectedCategoryLabel = "Tools";
        world.StockOnHand.SelectedBrandLabel = "Makita";
        world.StockOnHand.SelectedSupplierLabel = "Zenith Tools";
        world.StockOnHand.LocationText = "b2";
        world.StockOnHand.InStockOnly = true;
        await world.StockOnHand.RunCommand.ExecuteAsync(null);

        world.StockOnHandQuery.LastFilter.Should().Be(new Counterpoint.Application.Inventory.StockOnHandFilter(12, 21, 2, "b2", true));
        (world.Filters.CategoryCalls, world.Filters.BrandCalls, world.Filters.SupplierCalls).Should().Be((1, 1, 1));
    }

    [Fact]
    public async Task UI_14_ACustomRangeTheOwnerMistypedShowsAMessageAndNeverReachesTheQuery()
    {
        var world = new World(Role.Owner);
        world.Tax.Range.SelectedPresetLabel = "Custom range";

        world.Tax.Range.FromText = "06/09/2026";
        world.Tax.Range.ToText = "2026-09-07";
        await world.Tax.RunCommand.ExecuteAsync(null);
        world.Tax.Range.ValidationMessage.Should().Be("Type both dates as year-month-day, for example 2026-09-30.");

        world.Tax.Range.FromText = "2026-09-07";
        world.Tax.Range.ToText = "2026-09-06";
        await world.Tax.RunCommand.ExecuteAsync(null);
        world.Tax.Range.ValidationMessage.Should().Be("The end date cannot be before the start date.");

        world.TaxQuery.Calls.Should().Be(0);

        world.Tax.Range.FromText = "2026-09-06";
        world.Tax.Range.ToText = "2026-09-07";
        await world.Tax.RunCommand.ExecuteAsync(null);

        world.TaxQuery.Calls.Should().Be(1);
        world.TaxQuery.LastRange.Should().Be(ReportDateRange.Custom(new(2026, 9, 6), new(2026, 9, 7)));
        world.Tax.Range.ValidationMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task UI_14_ThePresetRangeIsResolvedAgainstTheClockAndReachesTheQuery()
    {
        var world = new World(Role.Owner);

        await world.Tender.RunCommand.ExecuteAsync(null);

        world.TenderQuery.LastRange.Should().Be(ReportDateRange.For(ReportDatePreset.ThisMonth, new DateOnly(2026, 9, 7)));
        world.TenderQuery.LastRange!.From.Should().Be(new DateOnly(2026, 9, 1));
        world.TenderQuery.LastRange.To.Should().Be(new DateOnly(2026, 9, 7));
    }

    // ---- Failure handling: plain sentences, detail in the log ------------------------------------

    [Fact]
    public async Task AC_17_ARefusalIsAPlainSentenceForEveryScreenNeverTheExceptionTextOrATypeName()
    {
        foreach (var screen in FailureScreens())
        {
            screen.SetFault(new NotAuthorisedException("IX.Method requires the owner role (Counterpoint.Application.Security.NotAuthorisedException)."));

            await screen.Run();

            screen.Screen.Status.Should().Be("The " + screen.Report + " report is for the owner. Sign in as the owner to see it.", screen.Report);
            screen.Screen.Status.Should().NotContain("Exception").And.NotContain("Counterpoint").And.NotContain("IX.Method").And.NotContain("role (");
            screen.Entries.Should().BeEmpty("a refusal is expected behaviour, not a fault to log: {0}", screen.Report);
            screen.Screen.Busy.Should().BeFalse();
        }
    }

    [Fact]
    public async Task UI_06_ADatabaseOrIoFailureIsAPlainReadFailureSentenceAndIsLoggedWithTheException()
    {
        foreach (var screen in FailureScreens())
        {
            foreach (Exception fault in new Exception[] { new FakeDbException("database disk image is malformed SECRET-DB-TEXT"), new IOException("disk I/O error SECRET-IO-TEXT") })
            {
                screen.Entries.Clear();
                screen.SetFault(fault);

                await screen.Run();

                screen.Screen.Status.Should().Be("The " + screen.Report + " report " + ReadFailed, screen.Report);
                screen.Screen.Status.Should().NotContain("SECRET").And.NotContain("Exception");
                screen.Entries.Should().ContainSingle(screen.Report);
                var entry = screen.Entries[0];
                entry.Level.Should().Be(LogLevel.Warning);
                entry.EventId.Id.Should().Be(3601);
                entry.Exception.Should().BeSameAs(fault, "the log gets the detail");
                entry.Message.Should().Be("The " + screen.Report + " report could not read the database.");
                screen.Screen.Busy.Should().BeFalse();
            }
        }
    }

    [Fact]
    public async Task UI_06_AnUnexpectedInvalidStateIsAPlainNotProducedSentenceAndLoggedAsAnError()
    {
        foreach (var screen in FailureScreens())
        {
            screen.Entries.Clear();
            var fault = new InvalidOperationException("the secret internal state INTERNAL-TEXT");
            screen.SetFault(fault);

            await screen.Run();

            screen.Screen.Status.Should().Be("The " + screen.Report + " report " + NotProduced, screen.Report);
            screen.Screen.Status.Should().NotContain("INTERNAL");
            var entry = screen.Entries.Should().ContainSingle(screen.Report).Subject;
            entry.Level.Should().Be(LogLevel.Error);
            entry.EventId.Id.Should().Be(3602);
            entry.Exception.Should().BeSameAs(fault);
        }
    }

    [Fact]
    public async Task UI_06_AScreenRecoversOnTheNextRunOnceTheFaultIsGoneAndIsLockedWhileItRuns()
    {
        var world = new World(Role.Owner);
        world.TaxQuery.Fault = new FakeDbException("locked");

        await world.Tax.RunCommand.ExecuteAsync(null);
        world.Tax.HasStatus.Should().BeTrue();

        world.TaxQuery.Fault = null;
        world.TaxQuery.Gate = new TaskCompletionSource();
        var running = world.Tax.RunCommand.ExecuteAsync(null);

        world.Tax.Busy.Should().BeTrue("the screen is locked while a read is in flight");
        world.Tax.Status.Should().BeEmpty("the old failure is cleared the moment a new run starts");

        world.TaxQuery.Gate.SetResult();
        await running;

        world.Tax.Busy.Should().BeFalse();
        world.Tax.HasStatus.Should().BeFalse();
        world.Tax.Rates.Rows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UI_06_ALookupFailureWhileFillingTheFilterListsIsHandledTheSameWay()
    {
        var logger = new CapturingLogger<StockOnHandViewModel>();
        var filters = new StubReportFilterLookup { Fault = new FakeDbException("lookup SECRET") };
        var screen = new StockOnHandViewModel(new StubStockOnHandQuery(), filters, logger);

        await screen.RunCommand.ExecuteAsync(null);

        screen.Status.Should().Be("The stock on hand report " + ReadFailed);
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    // ---- Navigation ------------------------------------------------------------------------------

    [AvaloniaFact]
    public void AC_17_TheRailOffersTheOwnerBothNewGroupsAndACashierOnlyStockOnHandAndTheReorderList()
    {
        var owner = RailState(new StubSession(Role.Owner));
        var cashier = RailState(new StubSession(Role.Cashier));

        owner.Headings.Should().Contain(["REPORTS", "STOCK REPORTS", "TAX AND CASH REPORTS"]);
        owner.Items.Should().Equal(
            "Sales summary", "Sales by item", "Profit", "Returns",
            "Stock on hand", "Reorder list", "Stock valuation", "Stock card", "Slow-moving stock", "Fast-moving items",
            "Damage and adjustments", "Supplier purchases", "Tax", "Tender reconciliation", "Shift variance");

        cashier.Headings.Should().Contain(["REPORTS", "STOCK REPORTS"]);
        cashier.Headings.Should().NotContain("TAX AND CASH REPORTS", "every tax and cash report is owner-only");
        cashier.Items.Should().Equal(
            ["Sales summary", "Sales by item", "Stock on hand", "Reorder list"],
            "the cost-free ones: Profit, Returns and every other stock, tax and cash report are hidden from a cashier as a courtesy");

        var signedOut = RailState(new SignedOutSession());
        signedOut.Items.Should().BeEmpty("nobody signed in sees no reports at all");
    }

    [AvaloniaFact]
    public void UI_16_EveryReportSectionNameTheRailOffersIsOneThisTaskBuiltAScreenFor()
    {
        BackOfficeShellViewModel.ReportSectionNames.Should().HaveCount(15);
        BackOfficeShellViewModel.ReportSectionNames.Should().Contain(Sections.Select(row => (string)row[0]));
        Sections.Select(row => (string)row[0]).Should().OnlyHaveUniqueItems();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Sections))]
    public void UI_16_SelectingAStockTaxOrCashSectionShowsExactlyThatScreenAndHidesTheOtherFourteen(string section, string expectedView, bool bothRoles)
    {
        _ = bothRoles;
        var world = new World(Role.Owner);
        var content = new ReportSectionContent { Reports = world.Reports, SelectedSection = section };
        var window = new Window { Content = content };
        window.Show();

        var screens = content.GetVisualDescendants().OfType<Control>()
            .Where(control => AllScreenViewNames.Contains(control.GetType().Name))
            .ToList();

        screens.Should().HaveCount(15, "all fifteen screens sit in the tree; only the selected one is visible");
        foreach (var screen in screens)
        {
            screen.IsVisible.Should().Be(screen.GetType().Name == expectedView, "'{0}' is selected, so only {1} shows", section, expectedView);
        }
    }

    [AvaloniaFact]
    public void UI_16_AContainerBuiltWithoutTheNewScreensStillLaysOutAndShowsNothingForTheirSections()
    {
        var world = new World(Role.Owner);
        var fourOnly = new ReportsViewModel(world.SalesSummary, world.SalesByItem, world.Profit, world.Returns);

        fourOnly.StockAndCash.Should().BeNull("the four-argument construction still works");

        var content = new ReportSectionContent { Reports = fourOnly, SelectedSection = "Tax" };
        var window = new Window { Content = content };

        var act = () => window.Show();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task UI_16_SelectingASectionInTheShellRunsItsScreenAndAnUnknownNameDoesNothing()
    {
        var session = new StubSession(Role.Owner);
        var world = new World(Role.Owner);
        var shell = new BackOfficeShellViewModel(session);
        shell.AttachReports(world.Reports);

        shell.SelectReportSectionCommand.Execute("Tax");
        await world.Tax.RunCommand.ExecutionTask!;

        shell.SelectedReportSection.Should().Be("Tax");
        shell.IsReportSectionActive.Should().BeTrue();
        shell.IsOverviewActive.Should().BeFalse();
        world.TaxQuery.Calls.Should().Be(1, "a fresh visit re-reads the report");
        world.Tax.Rates.Rows.Should().NotBeEmpty();

        shell.SelectReportSectionCommand.Execute("Tender reconciliation");
        await world.Tender.RunCommand.ExecutionTask!;
        world.TenderQuery.Calls.Should().Be(1);

        shell.SelectReportSectionCommand.Execute("Stock card");
        world.StockCardQuery.Calls.Should().Be(0, "the stock card waits for an item to be typed");

        var act = () => shell.SelectReportSectionCommand.Execute("No such report");
        act.Should().NotThrow();
        world.Reports.StockAndCash!.Load("No such report").Should().BeFalse();
    }

    [Fact]
    public async Task UI_16_TheContainerRunsTheScreenASectionNamesAndReturnsFalseForOneThatIsNotItsOwn()
    {
        var world = new World(Role.Owner);
        var container = world.Reports.StockAndCash!;

        foreach (var section in new[]
        {
            "Stock on hand", "Reorder list", "Stock valuation", "Slow-moving stock", "Fast-moving items", "Damage and adjustments",
            "Supplier purchases", "Tax", "Tender reconciliation", "Shift variance",
        })
        {
            container.Load(section).Should().BeTrue(section);
        }

        await Task.WhenAll(
            world.StockOnHand.RunCommand.ExecutionTask!, world.Reorder.RunCommand.ExecutionTask!, world.Valuation.RunCommand.ExecutionTask!,
            world.SlowMoving.RunCommand.ExecutionTask!, world.FastMoving.RunCommand.ExecutionTask!, world.Damage.RunCommand.ExecutionTask!,
            world.Purchases.RunCommand.ExecutionTask!, world.Tax.RunCommand.ExecutionTask!, world.Tender.RunCommand.ExecutionTask!,
            world.Variance.RunCommand.ExecutionTask!);

        new[]
        {
            world.StockOnHandQuery.Calls, world.ReorderQuery.Calls, world.ValuationQuery.Calls, world.SlowMovingQuery.Calls,
            world.FastMovingQuery.Calls, world.DamageQuery.Calls, world.PurchasesQuery.Calls, world.TaxQuery.Calls,
            world.TenderQuery.Calls, world.VarianceQuery.Calls,
        }.Should().OnlyContain(calls => calls == 1, "each section ran its own screen exactly once");

        container.Load("Stock card").Should().BeTrue("it is one of this container's screens, but needs an item typed first");
        world.StockCardQuery.Calls.Should().Be(0);
        container.Load("Sales summary").Should().BeFalse("a sales screen is ReportsViewModel's");
        container.Load("Nonsense").Should().BeFalse();
    }

    [Fact]
    public async Task UI_16_AFourArgumentReportsContainerStillLoadsTheSalesSectionsAndIgnoresTheNewOnes()
    {
        var world = new World(Role.Owner);
        var fourOnly = new ReportsViewModel(world.SalesSummary, world.SalesByItem, world.Profit, world.Returns);

        var act = () => fourOnly.Load("Tax");
        act.Should().NotThrow();
        world.TaxQuery.Calls.Should().Be(0, "no stock, tax and cash container, so nothing to run");

        fourOnly.Load("Profit");
        await world.Profit.RunCommand.ExecutionTask!;
        world.Profit.Rows.Should().NotBeEmpty("Load(\"Profit\") ran the profit screen");

        var fiveArg = world.Reports;
        fiveArg.StockAndCash.Should().BeSameAs(world.StockAndCash);
        fiveArg.Load("Tax");
        await world.Tax.RunCommand.ExecutionTask!;
        world.TaxQuery.Calls.Should().Be(1, "with the container attached the same Load reaches the new screen");
    }

    [Fact]
    public async Task AC_17_ACashierWhoReachesAnOwnerSectionAnyWayGetsTheRefusalSentenceFromTheServiceNotFigures()
    {
        // The rail hides Tax from a cashier as a courtesy; selecting it through the command anyway must still end at
        // the Application layer's refusal, which the screen shows as a sentence.
        var world = new World(Role.Cashier);
        world.TaxQuery.Fault = new NotAuthorisedException("not allowed");
        var shell = new BackOfficeShellViewModel(new StubSession(Role.Cashier));
        shell.AttachReports(world.Reports);

        shell.SelectReportSectionCommand.Execute("Tax");
        await world.Tax.RunCommand.ExecutionTask!;

        world.Tax.Status.Should().Be("The tax report is for the owner. Sign in as the owner to see it.");
        world.Tax.Rates.Rows.Should().BeEmpty();
        world.Tax.TotalSalesTaxText.Should().Be("-");
        world.Tax.NetTaxText.Should().Be("-");
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static HashSet<string> TextsOf(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty)];

    private static HashSet<string> VisibleTextsOf(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? string.Empty)];

    private static (List<string> Headings, List<string> Items) RailState(ISession session)
    {
        var shell = new BackOfficeShellViewModel(session);
        var rail = new NavRail { DataContext = shell };
        var window = new Window { Content = rail };
        window.Show();

        var headings = rail.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.Text is "REPORTS" or "STOCK REPORTS" or "TAX AND CASH REPORTS")
            .Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text!)
            .ToList();

        var items = rail.GetVisualDescendants().OfType<ToggleButton>()
            .Where(button => button.IsEffectivelyVisible
                && ReferenceEquals(button.Command, shell.SelectReportSectionCommand)
                && button.Content is string)
            .Select(button => (string)button.Content!)
            .ToList();

        return (headings, items);
    }

    private sealed record FailureScreen(
        string Report, ReportScreenViewModelBase Screen, Func<Task> Run, Action<Exception> SetFault, List<CapturedLog> Entries);

    private static FailureScreen Failing<TScreen, TStub>(
        string report, TStub stub, Func<TStub, ILogger<TScreen>, TScreen> create, Func<TScreen, Task> run)
        where TScreen : ReportScreenViewModelBase
        where TStub : FaultableStub
    {
        var logger = new CapturingLogger<TScreen>();
        var screen = create(stub, logger);

        return new FailureScreen(report, screen, () => run(screen), fault => stub.Fault = fault, logger.Entries);
    }

    /// <summary>One of every screen, each over a stub that can be told to fail and a logger that remembers what it was told.</summary>
    private static List<FailureScreen> FailureScreens() =>
    [
        Failing<TaxReportViewModel, StubTaxReportQuery>("tax", new StubTaxReportQuery(), (q, l) => new TaxReportViewModel(q, Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<TenderReconciliationViewModel, StubTenderReconciliationQuery>("tender reconciliation", new StubTenderReconciliationQuery(), (q, l) => new TenderReconciliationViewModel(q, Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<ShiftVarianceViewModel, StubShiftVarianceHistoryQuery>("shift and variance history", new StubShiftVarianceHistoryQuery(), (q, l) => new ShiftVarianceViewModel(q, Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<StockCardViewModel, StubStockCardQuery>("stock card", new StubStockCardQuery(), (q, l) => new StockCardViewModel(q, Clock, l) { SkuText = "RPT-BOLT-A" }, s => s.RunCommand.ExecuteAsync(null)),
        Failing<DamageAdjustmentViewModel, StubDamageAdjustmentReportQuery>("damage and adjustments", new StubDamageAdjustmentReportQuery(), (q, l) => new DamageAdjustmentViewModel(q, Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<SupplierPurchasesViewModel, StubSupplierPurchaseReportQuery>("supplier purchases", new StubSupplierPurchaseReportQuery(), (q, l) => new SupplierPurchasesViewModel(q, new StubReportFilterLookup(), Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<FastMovingViewModel, StubFastMovingReportQuery>("fast-moving items", new StubFastMovingReportQuery(), (q, l) => new FastMovingViewModel(q, Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<StockValuationViewModel, StubStockValuationQuery>("stock valuation", new StubStockValuationQuery(), (q, l) => new StockValuationViewModel(q, new StubReportFilterLookup(), Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<SlowMovingStockViewModel, StubSlowMovingStockQuery>("slow-moving stock", new StubSlowMovingStockQuery(), (q, l) => new SlowMovingStockViewModel(q, new StubReportFilterLookup(), Clock, l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<StockOnHandViewModel, StubStockOnHandQuery>("stock on hand", new StubStockOnHandQuery(), (q, l) => new StockOnHandViewModel(q, new StubReportFilterLookup(), l), s => s.RunCommand.ExecuteAsync(null)),
        Failing<ReorderListViewModel, StubReorderListQuery>("reorder list", new StubReorderListQuery(), (q, l) => new ReorderListViewModel(q, new StubReportFilterLookup(), l), s => s.RunCommand.ExecuteAsync(null)),
    ];

    /// <summary>All eleven screens, their stubs, and the two containers, built over one clock and one role.</summary>
    private sealed class World
    {
        internal World(Role role)
        {
            var bills = new StubBillQuery();
            ProfitQuery = new StubProfitQuery();

            SalesSummary = new SalesSummaryReportViewModel(new StubSalesSummaryQuery(), bills, Clock);
            SalesByItem = new SalesByItemReportViewModel(new StubBreakdownQuery(), ProfitQuery, bills, new StubSession(role), Clock);
            Profit = new ProfitReportViewModel(ProfitQuery, bills, Clock);
            Returns = new ReturnsReportViewModel(new StubReturnsQuery(), bills, Clock);

            StockOnHand = new StockOnHandViewModel(StockOnHandQuery, Filters);
            Reorder = new ReorderListViewModel(ReorderQuery, Filters);
            Valuation = new StockValuationViewModel(ValuationQuery, Filters, Clock);
            StockCard = new StockCardViewModel(StockCardQuery, Clock);
            SlowMoving = new SlowMovingStockViewModel(SlowMovingQuery, Filters, Clock);
            FastMoving = new FastMovingViewModel(FastMovingQuery, Clock);
            Damage = new DamageAdjustmentViewModel(DamageQuery, Clock);
            Purchases = new SupplierPurchasesViewModel(PurchasesQuery, Filters, Clock);
            Tax = new TaxReportViewModel(TaxQuery, Clock);
            Tender = new TenderReconciliationViewModel(TenderQuery, Clock);
            Variance = new ShiftVarianceViewModel(VarianceQuery, Clock);

            StockAndCash = new StockAndCashReportsViewModel(
                StockOnHand, Reorder, Valuation, StockCard, SlowMoving, FastMoving, Damage, Purchases, Tax, Tender, Variance);
            Reports = new ReportsViewModel(SalesSummary, SalesByItem, Profit, Returns, StockAndCash);
        }

        internal StubProfitQuery ProfitQuery { get; }

        internal StubReportFilterLookup Filters { get; } = new();

        internal StubStockOnHandQuery StockOnHandQuery { get; } = new();

        internal StubReorderListQuery ReorderQuery { get; } = new();

        internal StubStockValuationQuery ValuationQuery { get; } = new();

        internal StubStockCardQuery StockCardQuery { get; } = new();

        internal StubSlowMovingStockQuery SlowMovingQuery { get; } = new();

        internal StubFastMovingReportQuery FastMovingQuery { get; } = new();

        internal StubDamageAdjustmentReportQuery DamageQuery { get; } = new();

        internal StubSupplierPurchaseReportQuery PurchasesQuery { get; } = new();

        internal StubTaxReportQuery TaxQuery { get; } = new();

        internal StubTenderReconciliationQuery TenderQuery { get; } = new();

        internal StubShiftVarianceHistoryQuery VarianceQuery { get; } = new();

        internal SalesSummaryReportViewModel SalesSummary { get; }

        internal SalesByItemReportViewModel SalesByItem { get; }

        internal ProfitReportViewModel Profit { get; }

        internal ReturnsReportViewModel Returns { get; }

        internal StockOnHandViewModel StockOnHand { get; }

        internal ReorderListViewModel Reorder { get; }

        internal StockValuationViewModel Valuation { get; }

        internal StockCardViewModel StockCard { get; }

        internal SlowMovingStockViewModel SlowMoving { get; }

        internal FastMovingViewModel FastMoving { get; }

        internal DamageAdjustmentViewModel Damage { get; }

        internal SupplierPurchasesViewModel Purchases { get; }

        internal TaxReportViewModel Tax { get; }

        internal TenderReconciliationViewModel Tender { get; }

        internal ShiftVarianceViewModel Variance { get; }

        internal StockAndCashReportsViewModel StockAndCash { get; }

        internal ReportsViewModel Reports { get; }

        internal async Task RunAllAsync()
        {
            await StockOnHand.RunCommand.ExecuteAsync(null);
            await Reorder.RunCommand.ExecuteAsync(null);
            await Valuation.RunCommand.ExecuteAsync(null);
            await SlowMoving.RunCommand.ExecuteAsync(null);
            await FastMoving.RunCommand.ExecuteAsync(null);
            await Damage.RunCommand.ExecuteAsync(null);
            await Purchases.RunCommand.ExecuteAsync(null);
            await Tax.RunCommand.ExecuteAsync(null);
            await Tender.RunCommand.ExecuteAsync(null);
            await Variance.RunCommand.ExecuteAsync(null);
        }
    }

    private sealed class StockCashClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class StubSession(Role role) : ISession
    {
        public AuthenticatedUser? CurrentUser { get; } = new(1, "user", "User", role);

        public bool IsAuthenticated => true;

        public Role? Role => role;

        public long? ShiftId => null;
    }

    private sealed class SignedOutSession : ISession
    {
        public AuthenticatedUser? CurrentUser => null;

        public bool IsAuthenticated => false;

        public Role? Role => null;

        public long? ShiftId => null;
    }
}
