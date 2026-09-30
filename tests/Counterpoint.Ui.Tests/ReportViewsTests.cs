using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Ui.Tests.Support;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Reports;
using Counterpoint.Ui.Views;
using Counterpoint.Ui.Views.Reports;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T05's four report screens and their drill-down, built for real in a headless Avalonia
/// window: each view constructs and lays out, every input on it has a visible label (SRS UI-14,
/// AC-22), the bound figures actually reach the screen, and the section host and the nav rail show
/// exactly the right screen to the right role.
/// </summary>
public sealed class ReportViewsTests
{
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

    [AvaloniaFact]
    public void UI_14_EveryReportViewConstructsLaysOutAndHasNoUnlabelledInputWithoutADataContext()
    {
        Control[] views =
        [
            new SalesSummaryReportView(),
            new SalesByItemReportView(),
            new ProfitReportView(),
            new ReturnsReportView(),
            new BillDrillDownView(),
            new ReportRangeBar(),
            new ReportSectionContent(),
        ];

        foreach (var view in views)
        {
            ViewLabelInspector.FindInputsWithoutVisibleLabel(view)
                .Should().BeEmpty("{0} must label every input it carries (AC-22)", view.GetType().Name);
        }
    }

    [AvaloniaFact]
    public async Task UI_14_EveryReportViewHasNoUnlabelledInputOnceItIsBoundToARunningViewModel()
    {
        var bills = new StubBillQuery();
        var summary = new SalesSummaryReportViewModel(new StubSalesSummaryQuery(), bills, Clock);
        var byItem = new SalesByItemReportViewModel(new StubBreakdownQuery(), new StubProfitQuery(), bills, new StubSession(Role.Owner), Clock);
        var profit = new ProfitReportViewModel(new StubProfitQuery(), bills, Clock);
        var returns = new ReturnsReportViewModel(new StubReturnsQuery(), bills, Clock);

        await summary.RunCommand.ExecuteAsync(null);
        await byItem.RunCommand.ExecuteAsync(null);
        await profit.RunCommand.ExecuteAsync(null);
        await returns.RunCommand.ExecuteAsync(null);

        (Control View, object Context)[] bound =
        [
            (new SalesSummaryReportView(), summary),
            (new SalesByItemReportView(), byItem),
            (new ProfitReportView(), profit),
            (new ReturnsReportView(), returns),
        ];

        foreach (var (view, context) in bound)
        {
            view.DataContext = context;

            ViewLabelInspector.FindInputsWithoutVisibleLabel(view)
                .Should().BeEmpty("{0} must label every input, rows and all (AC-22)", view.GetType().Name);
        }
    }

    [AvaloniaFact]
    public async Task RPT_01_TheSalesSummaryViewShowsTheBoundFiguresAndTheDrillDownsRows()
    {
        var bills = new StubBillQuery();
        var viewModel = new SalesSummaryReportViewModel(new StubSalesSummaryQuery(), bills, Clock);
        await viewModel.RunCommand.ExecuteAsync(null);

        var view = new SalesSummaryReportView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = TextsOf(view);

        texts.Should().Contain("944.00", "the net sales headline, and the day row's net");
        texts.Should().Contain("1,000.00", "gross");
        texts.Should().Contain("56.00", "discounts");
        texts.Should().Contain("69.40", "tax");
        texts.Should().Contain("2026-09-06", "the by-day row");
        texts.Should().Contain("10:00-10:59", "the by-hour row");
        texts.Should().Contain("CASH", "the by-tender row");
    }

    [AvaloniaFact]
    public async Task RPT_01_TheProfitViewShowsCostAndMarginForTheOwnerScreen()
    {
        var viewModel = new ProfitReportViewModel(new StubProfitQuery(), new StubBillQuery(), Clock);
        await viewModel.RunCommand.ExecuteAsync(null);

        var view = new ProfitReportView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = TextsOf(view);

        texts.Should().Contain("546.00", "COGS");
        texts.Should().Contain("398.00", "gross profit");
        texts.Should().Contain("42.2%", "margin 398 / 944");
    }

    [AvaloniaFact]
    public async Task RPT_01_TheReturnsViewShowsTheRatesAndGroupRows()
    {
        var viewModel = new ReturnsReportViewModel(new StubReturnsQuery(), new StubBillQuery(), Clock);
        await viewModel.RunCommand.ExecuteAsync(null);

        var view = new ReturnsReportView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = TextsOf(view);

        texts.Should().Contain("435.50", "returns subtotal");
        texts.Should().Contain("479.05", "total refunded");
        texts.Should().Contain("27.3%", "value return rate 435.50 / 1594.00");
        texts.Should().Contain("60.0%", "count return rate 3 / 5");
        texts.Should().Contain("Cracked housing", "a reason row");
    }

    [AvaloniaFact]
    public async Task RPT_01_TheDrillDownViewShowsTheBillListThenTheBill()
    {
        var bills = new StubBillQuery();
        var drill = new BillDrillDownViewModel(bills);
        var view = new BillDrillDownView { DataContext = drill };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();

        await drill.OpenBillListAsync(
            new Counterpoint.Application.Reporting.BillListFilter(
                Counterpoint.Application.Reporting.ReportDateRange.Custom(StubReports.Day, StubReports.Day)),
            "Bills on 2026-09-06");
        Dispatcher.UIThread.RunJobs();

        TextsOf(view).Should().Contain("INV-2026-000001").And.Contain("INV-2026-000002").And.Contain("494.00", "the bill's net, which is what the list shows");

        await drill.OpenBillCommand.ExecuteAsync(drill.Bills[0]);
        Dispatcher.UIThread.RunJobs();

        bills.LastOpened.Should().Be(11);
        TextsOf(view).Should().Contain("Bolt", "the bill's own line").And.Contain("CASH", "and its payment").And.Contain("543.40", "and its total");
    }

    [AvaloniaTheory]
    [InlineData("Sales summary", "SalesSummaryReportView")]
    [InlineData("Sales by item", "SalesByItemReportView")]
    [InlineData("Profit", "ProfitReportView")]
    [InlineData("Returns", "ReturnsReportView")]
    public void UI_16_SelectingAReportSectionShowsExactlyThatScreenAndHidesTheOtherThree(string section, string expectedView)
    {
        BackOfficeShellViewModel.ReportSectionNames.Should().Contain(section, "the fixture must match the rail's own list");

        var content = new ReportSectionContent { Reports = BuildReports(new StubSession(Role.Owner)), SelectedSection = section };
        var window = new Window { Content = content };
        window.Show();

        var screens = content.GetVisualDescendants().OfType<Control>()
            .Where(control => control.GetType().Name is "SalesSummaryReportView" or "SalesByItemReportView" or "ProfitReportView" or "ReturnsReportView")
            .ToList();

        screens.Should().HaveCount(4, "all four screens sit in the tree; only the selected one is visible");
        foreach (var screen in screens)
        {
            screen.IsVisible.Should().Be(screen.GetType().Name == expectedView, "'{0}' is selected, so only {1} shows", section, expectedView);
        }
    }

    [AvaloniaFact]
    public void UI_16_NoReportSectionSelectedShowsNoScreen()
    {
        var content = new ReportSectionContent { Reports = BuildReports(new StubSession(Role.Owner)) };
        var window = new Window { Content = content };
        window.Show();

        content.GetVisualDescendants().OfType<Control>()
            .Where(control => control.GetType().Name.EndsWith("ReportView", StringComparison.Ordinal))
            .Should().OnlyContain(screen => !screen.IsVisible);
    }

    [AvaloniaFact]
    public async Task UI_16_TheRealShellWindowShowsTheSelectedReportAndHidesTheOverview()
    {
        var session = new StubSession(Role.Owner);
        var shell = new BackOfficeShellViewModel(session);
        shell.AttachReports(BuildReports(session));

        var window = new BackOfficeShellWindow { DataContext = shell };
        window.Show();

        var content = window.GetVisualDescendants().OfType<ReportSectionContent>().Single();
        content.IsVisible.Should().BeFalse("the pane shows Overview until a report is chosen");

        shell.SelectReportSectionCommand.Execute("Profit");
        await shell.Reports!.Profit.RunCommand.ExecutionTask!;
        Dispatcher.UIThread.RunJobs();

        content.IsVisible.Should().BeTrue();
        shell.IsOverviewActive.Should().BeFalse();
        window.GetVisualDescendants().OfType<ProfitReportView>().Single().IsVisible.Should().BeTrue();
        window.GetVisualDescendants().OfType<ReturnsReportView>().Single().IsVisible.Should().BeFalse();
        shell.Reports.Profit.Rows.Should().NotBeEmpty("selecting a report runs it");

        shell.SelectOverviewCommand.Execute(null);
        content.IsVisible.Should().BeFalse("Overview returns the pane to the dashboard");
    }

    [AvaloniaFact]
    public void AC_17_TheNavRailOffersTheOwnerFourReportsAndACashierOnlyTheTwoCostFreeOnes()
    {
        ReportToggleLabels(new StubSession(Role.Owner)).Should().Equal("Sales summary", "Sales by item", "Profit", "Returns");
        ReportToggleLabels(new StubSession(Role.Cashier)).Should().Equal(
            ["Sales summary", "Sales by item"], "Profit and Returns are hidden from a cashier as a courtesy - the services refuse regardless");
    }

    private static List<string> ReportToggleLabels(ISession session)
    {
        var shell = new BackOfficeShellViewModel(session);
        var rail = new NavRail { DataContext = shell };
        var window = new Window { Content = rail };
        window.Show();

        return
        [
            .. rail.GetVisualDescendants().OfType<ToggleButton>()
                .Where(button => button.Content is string label
                    && label is "Sales summary" or "Sales by item" or "Profit" or "Returns")
                .Where(button => button.IsEffectivelyVisible)
                .Select(button => (string)button.Content!),
        ];
    }

    private static ReportsViewModel BuildReports(ISession session)
    {
        var bills = new StubBillQuery();

        return new ReportsViewModel(
            new SalesSummaryReportViewModel(new StubSalesSummaryQuery(), bills, Clock),
            new SalesByItemReportViewModel(new StubBreakdownQuery(), new StubProfitQuery(), bills, session, Clock),
            new ProfitReportViewModel(new StubProfitQuery(), bills, Clock),
            new ReturnsReportViewModel(new StubReturnsQuery(), bills, Clock));
    }

    private static HashSet<string> TextsOf(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty)];

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
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
}
