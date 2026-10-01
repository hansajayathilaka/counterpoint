using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Reporting.Queries;
using Counterpoint.Ui.ViewModels.Reports;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// AC-17 and SRS FR-9.4 for the P3-T05 reports: "Cashier sessions cannot open RPT-03
/// (service-level test)", and the cashier-safe DTOs carry no cost or margin field. There is no
/// viewmodel and no window in the service-level tests - they call the registered Application
/// service directly, the way a rogue caller would, through the real DI registration and its
/// <c>RoleAuthorisation</c> decorator.
/// </summary>
public sealed class SalesReportAuthorisationTests
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static readonly string[] CostWords = ["cost", "cogs", "profit", "margin"];

    // ---- Service level: a cashier session ------------------------------------------------------

    [Fact]
    public async Task AC_17_ACashierSessionCannotOpenTheProfitReportInAnyGrouping()
    {
        await using var fixture = await CashierWorldAsync();
        var profit = fixture.Resolve<IProfitReportQuery>();

        foreach (var grouping in Enum.GetValues<ProfitGrouping>())
        {
            Func<Task> act = () => profit.GetProfitReportAsync(BothDays, grouping);

            var thrown = await act.Should().ThrowAsync<NotAuthorisedException>("RPT-03 is owner-only ({0})", grouping);
            thrown.Which.Message.Should().Contain("IProfitReportQuery.GetProfitReportAsync").And.Contain("owner");
        }
    }

    [Fact]
    public async Task AC_17_ACashierSessionCannotOpenTheReturnsReport()
    {
        await using var fixture = await CashierWorldAsync();

        Func<Task> act = () => fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(BothDays);

        var thrown = await act.Should().ThrowAsync<NotAuthorisedException>("the returns report is owner-only (SRS section 9 RPT-14)");
        thrown.Which.Message.Should().Contain("IReturnsReportQuery.GetReturnsReportAsync").And.Contain("owner");
    }

    [Fact]
    public async Task AC_17_ACashierSessionStillGetsTheCostFreeReports()
    {
        // The guard must be a permission check, not a wall: the cashier-safe reports work, and the
        // figures are the hand-worked ones.
        await using var fixture = await CashierWorldAsync();

        var summary = await fixture.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);
        summary.Totals.NetSales.Should().Be(Money.FromDecimal(1158.50m));
        summary.ByDay.Should().HaveCount(2);

        var breakdown = await fixture.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(BothDays, SalesBreakdownDimension.Item);
        breakdown.TotalNet.Should().Be(Money.FromDecimal(1158.50m));
        breakdown.Rows.Should().HaveCount(4);

        var bills = await fixture.Resolve<ISalesBillQuery>().GetBillsAsync(new BillListFilter(BothDays));
        bills.Rows.Should().HaveCount(5);

        var detail = await fixture.Resolve<ISalesBillQuery>().GetBillAsync(bills.Rows[0].SaleId);
        detail!.Lines.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AC_17_NobodySignedInCannotOpenTheOwnerOnlyReportsEither()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await fixture.Resolve<IAuthenticationService>().LogOutAsync();

        Func<Task> profit = () => fixture.Resolve<IProfitReportQuery>().GetProfitReportAsync(BothDays, ProfitGrouping.Item);
        Func<Task> returns = () => fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(BothDays);

        await profit.Should().ThrowAsync<NotAuthorisedException>();
        await returns.Should().ThrowAsync<NotAuthorisedException>();
    }

    [Fact]
    public async Task AC_17_TheOwnerSessionOpensBothOwnerOnlyReports()
    {
        // The control for the refusals above.
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.BuildAsync(fixture);

        var profit = await fixture.Resolve<IProfitReportQuery>().GetProfitReportAsync(BothDays, ProfitGrouping.Item);
        var returns = await fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(BothDays);

        profit.Totals.GrossProfit.Should().Be(Money.FromDecimal(352.50m));
        returns.ReturnCount.Should().Be(3);
    }

    [Fact]
    public async Task AC_17_OnlyTheRoleDecoratedInterfacesAreRegisteredNeverTheConcreteQueriesOrTheirReaders()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        fixture.TryResolve<ProfitReportQuery>().Should().BeNull();
        fixture.TryResolve<ReturnsReportQuery>().Should().BeNull();
        fixture.TryResolve<SalesSummaryReportQuery>().Should().BeNull();
        fixture.TryResolve<SalesBillQuery>().Should().BeNull();
        fixture.TryResolve<SalesBreakdownQuery>().Should().BeNull();
        fixture.TryResolve<ItemFiguresReader>().Should().BeNull("it reads unit_cost with no role check of its own");
        fixture.TryResolve<DailyFiguresReader>().Should().BeNull("it reads sale.cogs with no role check of its own");

        fixture.Resolve<IProfitReportQuery>().GetType().Should().NotBe<ProfitReportQuery>("it is the role-authorisation proxy");
        fixture.Resolve<IReturnsReportQuery>().GetType().Should().NotBe<ReturnsReportQuery>("it is the role-authorisation proxy");
    }

    // ---- Declared gate -------------------------------------------------------------------------

    [Fact]
    public void AC_17_TheOwnerOnlyQueriesDeclareTheOwnerRoleAndTheCashierSafeOnesDeclareNone()
    {
        typeof(IProfitReportQuery).GetCustomAttribute<RequiresRoleAttribute>()!.Role.Should().Be(Role.Owner);
        typeof(IReturnsReportQuery).GetCustomAttribute<RequiresRoleAttribute>()!.Role.Should().Be(Role.Owner);

        typeof(ISalesSummaryReportQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
        typeof(ISalesBillQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
        typeof(ISalesBreakdownQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
    }

    // ---- Cashier-safe projections: no cost field at all ----------------------------------------

    [Theory]
    [InlineData(typeof(ISalesSummaryReportQuery))]
    [InlineData(typeof(ISalesBillQuery))]
    [InlineData(typeof(ISalesBreakdownQuery))]
    public void AC_17_EveryDtoACashierSafeReportCanReturnHasNoCostMarginCogsOrProfitProperty(Type queryInterface)
    {
        var reachable = ReachableReportingTypes(queryInterface);

        reachable.Should().NotBeEmpty();

        var offenders = reachable
            .SelectMany(type => type.GetProperties().Select(property => type.Name + "." + property.Name))
            .Where(name => CostWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.Should().BeEmpty(
            "a cashier-safe report's DTO has no cost-bearing property at all - the projection drops it (CLAUDE.md invariant 8, AC-17)");
    }

    [Fact]
    public void AC_17_TheBillListFilterAndTheReturnsReportCarryNoCostFieldEither()
    {
        // The returns report is owner-only for its SRS role column, not because it carries cost -
        // the rows are value and quantity only.
        var types = ReachableReportingTypes(typeof(IReturnsReportQuery))
            .Append(typeof(BillListFilter))
            .Distinct();

        types.SelectMany(type => type.GetProperties().Select(property => type.Name + "." + property.Name))
            .Where(name => CostWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Should().BeEmpty();
    }

    [Fact]
    public void AC_17_TheControlTheOwnerProfitDtoDoesCarryTheCostFigures()
    {
        // Proves the reflection walk above can see a cost field when there is one.
        var profitFields = ReachableReportingTypes(typeof(IProfitReportQuery))
            .SelectMany(type => type.GetProperties().Select(property => property.Name))
            .ToList();

        profitFields.Should().Contain(["Cogs", "GrossProfit", "MarginRate"]);
    }

    // ---- Viewmodel level: the UI surfaces the refusal, it does not bypass it -------------------

    [Fact]
    public async Task AC_17_TheProfitAndReturnsScreensShowTheRefusalAndNoFiguresToACashier()
    {
        await using var fixture = await CashierWorldAsync();
        var clock = new FixedTimeProvider(SalesReportDataset.At(8, 9, 0));

        var profit = new ProfitReportViewModel(fixture.Resolve<IProfitReportQuery>(), fixture.Resolve<ISalesBillQuery>(), clock);
        SelectCustomRange(profit.Range);
        await profit.RunCommand.ExecuteAsync(null);

        profit.HasStatus.Should().BeTrue();
        profit.Status.Should().Contain("owner");
        profit.Rows.Should().BeEmpty();
        profit.CogsText.Should().Be("-");
        profit.GrossProfitText.Should().Be("-");
        profit.MarginText.Should().Be("-");

        var returns = new ReturnsReportViewModel(fixture.Resolve<IReturnsReportQuery>(), fixture.Resolve<ISalesBillQuery>(), clock);
        SelectCustomRange(returns.Range);
        await returns.RunCommand.ExecuteAsync(null);

        returns.HasStatus.Should().BeTrue();
        returns.Status.Should().Contain("owner");
        returns.ByReason.Should().BeEmpty();
        returns.ReturnCountText.Should().Be("-");
    }

    [Fact]
    public async Task AC_17_TheSalesByItemScreenRefusesToShowMarginToACashierEvenIfTheToggleIsForcedOn()
    {
        await using var fixture = await CashierWorldAsync();
        var clock = new FixedTimeProvider(SalesReportDataset.At(8, 9, 0));

        var screen = new SalesByItemReportViewModel(
            fixture.Resolve<ISalesBreakdownQuery>(),
            fixture.Resolve<IProfitReportQuery>(),
            fixture.Resolve<ISalesBillQuery>(),
            fixture.Resolve<ISession>(),
            clock);

        screen.CanShowMargin.Should().BeFalse("the toggle is hidden from a cashier");

        // Forced on regardless (a bypass of the courtesy hiding): the service still refuses.
        screen.ShowMargin = true;
        SelectCustomRange(screen.Range);
        await screen.RunCommand.ExecuteAsync(null);

        screen.MarginRows.Should().BeEmpty("no cost figure may reach a cashier screen");
        screen.HasStatus.Should().BeTrue();
        screen.Status.Should().Contain("owner");

        // And with the toggle off the cashier gets the cost-free rows.
        screen.ShowMargin = false;
        await screen.RunCommand.ExecuteAsync(null);

        screen.HasStatus.Should().BeFalse();
        screen.Rows.Should().HaveCount(4);
    }

    private static void SelectCustomRange(ReportRangeViewModel range)
    {
        range.SelectedPresetLabel = "Custom range";
        range.FromText = "2026-09-06";
        range.ToText = "2026-09-07";
    }

    private static async Task<SaleFixture> CashierWorldAsync()
    {
        var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        try
        {
            await SalesReportDataset.BuildAsync(fixture);

            await fixture.Resolve<IUserAdministration>()
                .CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

            var authentication = fixture.Resolve<IAuthenticationService>();
            await authentication.LogOutAsync();
            (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

            fixture.Resolve<ISession>().Role.Should().Be(Role.Cashier);

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>Every DTO type reachable from the return types of an interface's methods, in the reporting namespace.</summary>
    private static List<Type> ReachableReportingTypes(Type queryInterface)
    {
        var found = new HashSet<Type>();

        foreach (var method in queryInterface.GetMethods())
        {
            Walk(method.ReturnType, found);
        }

        return [.. found];
    }

    private static void Walk(Type type, HashSet<Type> found)
    {
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Walk(argument, found);
            }

            return;
        }

        if (type.Namespace != typeof(ISalesBillQuery).Namespace || !type.IsClass || !found.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties())
        {
            Walk(property.PropertyType, found);
        }
    }
}
