using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Reporting;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Reports;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// The four P3-T05 report viewmodels and the shell's Reports navigation over the real report
/// queries and the hand-worked dataset: a summary row reaches the correct bill (task P3-T05
/// "Done when" #4), and the figures on the screen are the hand-worked ones formatted.
/// </summary>
[Collection(SalesReportFixture.Name)]
public sealed class ReportDrillDownEndToEndTests(SalesReportFixture fixture)
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(SalesReportDataset.At(8, 9, 0));

    private static void SelectBothDays(ReportRangeViewModel range)
    {
        range.SelectedPresetLabel = "Custom range";
        range.FromText = "2026-09-06";
        range.ToText = "2026-09-07";
    }

    private SalesSummaryReportViewModel Summary() =>
        new(fixture.Host.Resolve<ISalesSummaryReportQuery>(), fixture.Host.Resolve<ISalesBillQuery>(), Clock);

    [Fact]
    public async Task RPT_01_TheSummaryScreenShowsTheHandWorkedFigures()
    {
        var screen = Summary();
        SelectBothDays(screen.Range);

        await screen.RunCommand.ExecuteAsync(null);

        screen.HasStatus.Should().BeFalse();
        screen.BillCountText.Should().Be("5");
        screen.AverageBillText.Should().Be("318.80");
        screen.GrossText.Should().Be("1,650.00");
        screen.DiscountsText.Should().Be("56.00");
        screen.TaxText.Should().Be("129.40");
        screen.NetText.Should().Be("1,158.50");
        screen.ReturnsText.Should().Be("479.05");
        screen.TenderTotalText.Should().Be("1,244.35");
        screen.ByDay.Select(row => (row.DateText, row.BillCountText, row.NetText)).Should().Equal(
            [("2026-09-06", "3", "944.00"), ("2026-09-07", "2", "214.50")]);
        screen.ByHour.Select(row => row.HourText).Should().Equal("09:00-09:59", "10:00-10:59", "11:00-11:59", "14:00-14:59", "16:00-16:59");
        screen.ByTender.Select(row => (row.TenderType, row.NetText)).Should().Equal([("CARD", "253.40"), ("CASH", "990.95")]);
    }

    [Fact]
    public async Task RPT_01_DrillingADayRowThenABillRowReachesTheCorrectBillAndBackAgain()
    {
        var data = fixture.Data;
        var screen = Summary();
        SelectBothDays(screen.Range);
        await screen.RunCommand.ExecuteAsync(null);

        var dayOne = screen.ByDay.Single(row => row.Date == SalesReportDataset.DayOne);
        await screen.DrillDayCommand.ExecuteAsync(dayOne);

        screen.Drill.Level.Should().Be(DrillLevel.BillList);
        screen.Drill.Title.Should().Be("Bills on 2026-09-06");
        screen.Drill.Bills.Select(row => row.SaleId).Should().Equal([data.B1.SaleId, data.B2.SaleId, data.B3.SaleId]);
        screen.Drill.Bills.Select(row => row.NetText).Should().Equal(["494.00", "250.00", "200.00"]);

        var b2Row = screen.Drill.Bills.Single(row => row.BillNo == data.B2.BillNo);
        await screen.Drill.OpenBillCommand.ExecuteAsync(b2Row);

        screen.Drill.Level.Should().Be(DrillLevel.Bill);
        var bill = screen.Drill.Bill!;
        bill.BillNo.Should().Be(data.B2.BillNo);
        bill.TotalText.Should().Be("250.00");
        bill.Lines.Select(line => (line.Description.Contains("Nail"), line.QuantityText, line.UnitPriceText, line.LineTotalText)).First()
            .Should().Be((true, "2 box", "100.00", "200.00"));
        bill.Lines.Should().HaveCount(2);
        bill.Lines[1].Description.Should().Be("Delivery");
        bill.Payments.Should().ContainSingle().Which.Should().Be(new BillPaymentRow("CASH", "250.00"));
        bill.HasReturns.Should().BeFalse();

        screen.Drill.BackCommand.Execute(null);
        screen.Drill.Level.Should().Be(DrillLevel.BillList);
        screen.Drill.Bills.Should().HaveCount(3);

        screen.Drill.BackCommand.Execute(null);
        screen.Drill.Level.Should().Be(DrillLevel.None);
    }

    [Fact]
    public async Task RPT_01_ABillWithAReturnShowsTheReturnAndTheReturnedQuantityOnItsLine()
    {
        var data = fixture.Data;
        var screen = Summary();
        SelectBothDays(screen.Range);
        await screen.RunCommand.ExecuteAsync(null);

        await screen.Drill.OpenBillByIdAsync(data.B1.SaleId, fromList: false);

        var bill = screen.Drill.Bill!;
        bill.HeadingText.Should().Be("Bill " + data.B1.BillNo);
        bill.HasReturns.Should().BeTrue();
        bill.Returns.Should().ContainSingle().Which.Should().Be(
            new BillReturnRow(data.R1.ReturnNo, "2026-09-07", "CASH", "94.05"));
        bill.Lines[0].ReturnedText.Should().Be("1 returned");
        bill.Lines[1].ReturnedText.Should().BeEmpty();
        bill.SubtotalText.Should().Be("520.00");
        bill.LineDiscountText.Should().Be("30.00");
        bill.BillDiscountText.Should().Be("26.00");
        bill.TaxText.Should().Be("49.40");
    }

    [Fact]
    public async Task RPT_01_DrillingAnHourRowListsThatHoursBills()
    {
        var data = fixture.Data;
        var screen = Summary();
        SelectBothDays(screen.Range);
        await screen.RunCommand.ExecuteAsync(null);

        await screen.DrillHourCommand.ExecuteAsync(screen.ByHour.Single(row => row.Hour == 14));

        screen.Drill.Bills.Select(row => row.SaleId).Should().Equal([data.B3.SaleId, data.B6.SaleId]);
        screen.Drill.Title.Should().Contain("14:00-14:59").And.Contain("2026-09-06").And.Contain("2026-09-07");

        await screen.Drill.OpenBillCommand.ExecuteAsync(screen.Drill.Bills[1]);
        screen.Drill.Bill!.BillNo.Should().Be(data.B6.BillNo, "the second row is day two's 14:10 bill, not the first row's bill");
    }

    [Fact]
    public async Task RPT_01_TheAllBillsButtonListsEveryBillInTheLastRunRange()
    {
        var screen = Summary();
        SelectBothDays(screen.Range);

        screen.ShowAllBillsCommand.Execute(null);
        screen.Drill.Level.Should().Be(DrillLevel.None, "nothing has been run yet, so there is no range to list");

        await screen.RunCommand.ExecuteAsync(null);
        await screen.ShowAllBillsCommand.ExecuteAsync(null);

        screen.Drill.Bills.Should().HaveCount(5);
    }

    [Fact]
    public async Task RPT_02_DrillingAnItemRowListsTheBillsThatSoldIt()
    {
        var data = fixture.Data;
        var screen = new SalesByItemReportViewModel(
            fixture.Host.Resolve<ISalesBreakdownQuery>(),
            fixture.Host.Resolve<IProfitReportQuery>(),
            fixture.Host.Resolve<ISalesBillQuery>(),
            fixture.Host.Resolve<ISession>(),
            Clock);
        SelectBothDays(screen.Range);

        screen.CanShowMargin.Should().BeTrue("the session is the owner's");
        await screen.RunCommand.ExecuteAsync(null);

        screen.TotalNetText.Should().Be("1,158.50");
        screen.Rows.Select(row => (row.Name, row.NetText, row.QuantityText)).Should().Equal(
            [("Drill", "487.50", "2 pc"), ("Bolt", "371.00", "4 pc"), ("Nail", "250.00", "29 pc"), ("(Open items)", "50.00", "1")]);
        screen.Rows.Select(row => row.CanDrill).Should().Equal([true, true, true, false], "an open item has no variant to drill to");

        var bolt = screen.Rows.Single(row => row.Key == data.BoltVariantId);
        await screen.DrillCommand.ExecuteAsync(bolt);

        screen.Drill.Bills.Select(row => row.SaleId).Should().Equal([data.B1.SaleId, data.B3.SaleId, data.B6.SaleId]);
        screen.Drill.Title.Should().StartWith("Bills containing Bolt");

        screen.Drill.CloseCommand.Execute(null);
        await screen.DrillCommand.ExecuteAsync(screen.Rows[3]);
        screen.Drill.Level.Should().Be(DrillLevel.None, "the open-item row drills nowhere");
    }

    [Fact]
    public async Task RPT_02_CategoryAndBrandRowsDoNotDrillAndTheOwnerCanSwitchMarginOn()
    {
        var screen = new SalesByItemReportViewModel(
            fixture.Host.Resolve<ISalesBreakdownQuery>(),
            fixture.Host.Resolve<IProfitReportQuery>(),
            fixture.Host.Resolve<ISalesBillQuery>(),
            fixture.Host.Resolve<ISession>(),
            Clock);
        SelectBothDays(screen.Range);

        screen.SelectedDimensionLabel = "By category";
        await screen.RunCommand.ExecuteAsync(null);
        screen.Rows.Select(row => row.Name).Should().Equal(["Tools", "Fasteners", "(No category)"]);
        screen.Rows.Should().OnlyContain(row => !row.CanDrill, "a category is not a variant, so there is no bill filter for it");

        screen.ShowMargin = true;
        await screen.RunCommand.ExecuteAsync(null);

        screen.ShowPlainRows.Should().BeFalse();
        screen.Rows.Should().BeEmpty();
        screen.MarginRows.Select(row => (row.Name, row.NetText, row.CogsText, row.GrossProfitText)).Should().Equal(
            [
                ("(No category)", "300.00", "116.00", "184.00"),
                ("Fasteners", "371.00", "240.00", "131.00"),
                ("Tools", "487.50", "450.00", "37.50"),
            ]);
        screen.TotalNetText.Should().Be("1,158.50");
    }

    [Fact]
    public async Task RPT_03_TheProfitScreenShowsTheHandWorkedFiguresAndDrillsAPeriodRow()
    {
        var data = fixture.Data;
        var screen = new ProfitReportViewModel(
            fixture.Host.Resolve<IProfitReportQuery>(), fixture.Host.Resolve<ISalesBillQuery>(), Clock);
        SelectBothDays(screen.Range);

        screen.SelectedGroupingLabel = "By day";
        await screen.RunCommand.ExecuteAsync(null);

        screen.NetSalesText.Should().Be("1,158.50");
        screen.CogsText.Should().Be("806.00");
        screen.GrossProfitText.Should().Be("352.50");
        screen.MarginText.Should().Be("30.4%");
        screen.DiscountsText.Should().Be("56.00");
        screen.ReturnsText.Should().Be("479.05");
        screen.Rows.Select(row => (row.Name, row.NetText, row.CogsText, row.GrossProfitText, row.MarginText)).Should().Equal(
            [("2026-09-06", "944.00", "546.00", "398.00", "42.2%"), ("2026-09-07", "214.50", "260.00", "-45.50", "-21.2%")]);

        await screen.DrillCommand.ExecuteAsync(screen.Rows[1]);

        screen.Drill.Bills.Select(row => row.SaleId).Should().Equal(
            [data.B5.SaleId, data.B6.SaleId], "drilling day two lists day two's bills only");

        screen.Drill.CloseCommand.Execute(null);
        screen.SelectedGroupingLabel = "By item";
        await screen.RunCommand.ExecuteAsync(null);
        var nail = screen.Rows.Single(row => row.Key == data.NailVariantId);
        await screen.DrillCommand.ExecuteAsync(nail);

        screen.Drill.Bills.Select(row => row.SaleId).Should().Equal([data.B2.SaleId, data.B6.SaleId]);
    }

    [Fact]
    public async Task RPT_14_TheReturnsScreenShowsTheHandWorkedFigures()
    {
        var screen = new ReturnsReportViewModel(
            fixture.Host.Resolve<IReturnsReportQuery>(), fixture.Host.Resolve<ISalesBillQuery>(), Clock);
        SelectBothDays(screen.Range);

        await screen.RunCommand.ExecuteAsync(null);

        screen.ReturnCountText.Should().Be("3");
        screen.ReturnsSubtotalText.Should().Be("435.50");
        screen.TotalRefundedText.Should().Be("479.05");
        screen.ValueRateText.Should().Be("27.3%");
        screen.CountRateText.Should().Be("60.0%");
        screen.ByDisposition.Select(row => (row.Key, row.CountText, row.ValueText)).Should().Equal(
            [("DAMAGED", "1", "250.00"), ("SELLABLE", "2", "185.50")]);
        screen.ByLinkage.Select(row => (row.Key, row.CountText, row.ValueText)).Should().Equal(
            [("Linked", "2", "335.50"), ("Unlinked", "1", "100.00")]);
        screen.ByReason.Select(row => row.Key).Should().Equal(["Cracked housing", "No receipt", "Changed mind"]);
    }

    [Fact]
    public async Task RPT_01_AnEmptyRangeShowsAStatusNotAnError()
    {
        var screen = Summary();
        screen.Range.SelectedPresetLabel = "Custom range";
        screen.Range.FromText = "2026-01-01";
        screen.Range.ToText = "2026-01-31";

        await screen.RunCommand.ExecuteAsync(null);

        screen.Status.Should().Be("No sales or returns in this range.");
        screen.BillCountText.Should().Be("0");
        screen.AverageBillText.Should().Be("0.00");
        screen.ByDay.Should().BeEmpty();
    }

    [Fact]
    public async Task UI_16_SelectingAReportInTheShellRunsItAndLeavingItClosesAnyDrillDown()
    {
        var session = fixture.Host.Resolve<ISession>();
        var bills = fixture.Host.Resolve<ISalesBillQuery>();
        var reports = new ReportsViewModel(
            new SalesSummaryReportViewModel(fixture.Host.Resolve<ISalesSummaryReportQuery>(), bills, Clock),
            new SalesByItemReportViewModel(
                fixture.Host.Resolve<ISalesBreakdownQuery>(), fixture.Host.Resolve<IProfitReportQuery>(), bills, session, Clock),
            new ProfitReportViewModel(fixture.Host.Resolve<IProfitReportQuery>(), bills, Clock),
            new ReturnsReportViewModel(fixture.Host.Resolve<IReturnsReportQuery>(), bills, Clock));
        SelectBothDays(reports.SalesSummary.Range);
        SelectBothDays(reports.Returns.Range);

        var shell = new BackOfficeShellViewModel(session);
        shell.AttachReports(reports);

        shell.CanViewReports.Should().BeTrue();
        shell.CanViewOwnerReports.Should().BeTrue();
        shell.IsReportSectionActive.Should().BeFalse();
        shell.IsOverviewActive.Should().BeTrue();

        shell.SelectReportSectionCommand.Execute(ReportsViewModel.SalesSummarySection);
        await reports.SalesSummary.RunCommand.ExecutionTask!;

        shell.SelectedReportSection.Should().Be("Sales summary");
        shell.IsReportSectionActive.Should().BeTrue();
        shell.IsOverviewActive.Should().BeFalse();
        reports.SalesSummary.NetText.Should().Be("1,158.50", "selecting a report ran it");

        await reports.SalesSummary.ShowAllBillsCommand.ExecuteAsync(null);
        reports.SalesSummary.Drill.IsActive.Should().BeTrue();

        // Another section, another visit: the open drill-down does not greet the next visit.
        shell.SelectReportSectionCommand.Execute(ReportsViewModel.ReturnsSection);
        await reports.Returns.RunCommand.ExecutionTask!;

        shell.SelectedReportSection.Should().Be("Returns");
        reports.SalesSummary.Drill.IsActive.Should().BeFalse("a fresh visit starts at the report, not on a stale drill-down");
        reports.Returns.ReturnCountText.Should().Be("3");

        shell.SelectOverviewCommand.Execute(null);
        shell.SelectedReportSection.Should().BeNull();
        shell.IsReportSectionActive.Should().BeFalse();
        shell.IsOverviewActive.Should().BeTrue();
    }

    [Fact]
    public async Task UI_16_AReportSectionAndACatalogueSectionNeverShowTogether()
    {
        var session = fixture.Host.Resolve<ISession>();
        var bills = fixture.Host.Resolve<ISalesBillQuery>();
        var reports = new ReportsViewModel(
            new SalesSummaryReportViewModel(fixture.Host.Resolve<ISalesSummaryReportQuery>(), bills, Clock),
            new SalesByItemReportViewModel(
                fixture.Host.Resolve<ISalesBreakdownQuery>(), fixture.Host.Resolve<IProfitReportQuery>(), bills, session, Clock),
            new ProfitReportViewModel(fixture.Host.Resolve<IProfitReportQuery>(), bills, Clock),
            new ReturnsReportViewModel(fixture.Host.Resolve<IReturnsReportQuery>(), bills, Clock));
        var shell = new BackOfficeShellViewModel(session);
        shell.AttachReports(reports);

        shell.SelectReportSectionCommand.Execute(ReportsViewModel.ProfitSection);
        await reports.Profit.RunCommand.ExecutionTask!;

        shell.SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[0];

        shell.SelectedReportSection.Should().BeNull("choosing Catalogue leaves Reports");
        shell.IsCatalogueSectionActive.Should().BeTrue();
        shell.IsReportSectionActive.Should().BeFalse();

        shell.SelectReportSectionCommand.Execute(ReportsViewModel.SalesByItemSection);
        await reports.SalesByItem.RunCommand.ExecutionTask!;

        shell.SelectedCatalogueSection.Should().BeNull("choosing a report leaves Catalogue");
        shell.IsCatalogueSectionActive.Should().BeFalse();
        shell.IsReportSectionActive.Should().BeTrue();
    }

    // ---- The shared range picker ---------------------------------------------------------------

    [Fact]
    public void FR_9_1_ThePresetsResolveThroughTheOneApplicationLayerRangeRule()
    {
        // 2026-09-08 is a Tuesday, so this week started on Monday 2026-09-07.
        var range = new ReportRangeViewModel(new FixedTimeProvider(SalesReportDataset.At(8, 9, 0)));

        range.PresetLabels.Should().Equal(
            ["Today", "Yesterday", "This week", "This month", "Last month", "This year", "Custom range"]);
        range.SelectedPreset.Should().Be(ReportDatePreset.ThisMonth, "the default");

        (string Label, string From, string To)[] expected =
        [
            ("Today", "2026-09-08", "2026-09-08"),
            ("Yesterday", "2026-09-07", "2026-09-07"),
            ("This week", "2026-09-07", "2026-09-08"),
            ("This month", "2026-09-01", "2026-09-08"),
            ("Last month", "2026-08-01", "2026-08-31"),
            ("This year", "2026-01-01", "2026-09-08"),
        ];

        foreach (var (label, from, to) in expected)
        {
            range.SelectedPresetLabel = label;

            range.TryResolve(out var resolved).Should().BeTrue();
            resolved.From.Should().Be(DateOnly.ParseExact(from, "yyyy-MM-dd"), label);
            resolved.To.Should().Be(DateOnly.ParseExact(to, "yyyy-MM-dd"), label);
        }
    }

    [Theory]
    [InlineData("2026-13-01", "2026-09-01", "year-month-day")]
    [InlineData("2026-09-01", "garbage", "year-month-day")]
    [InlineData("2026-09-10", "2026-09-01", "cannot be before")]
    public void FR_9_1_ABadCustomRangeIsRefusedWithAMessageAndNoQueryIsRun(string from, string to, string expectedMessage)
    {
        var range = new ReportRangeViewModel(Clock)
        {
            SelectedPresetLabel = "Custom range",
            FromText = from,
            ToText = to,
        };

        range.TryResolve(out _).Should().BeFalse();
        range.ValidationMessage.Should().Contain(expectedMessage);
    }

    [Fact]
    public async Task FR_9_1_ABadCustomRangeOnAScreenRunsNothingAndLeavesTheFiguresAlone()
    {
        var screen = Summary();
        SelectBothDays(screen.Range);
        await screen.RunCommand.ExecuteAsync(null);
        screen.NetText.Should().Be("1,158.50");

        screen.Range.ToText = "not a date";
        await screen.RunCommand.ExecuteAsync(null);

        screen.NetText.Should().Be("1,158.50", "a refused range must not blank or change what is on screen");
        screen.Range.ValidationMessage.Should().NotBeEmpty();
    }
}
