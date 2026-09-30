using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Reporting.Inventory;
using Counterpoint.Reporting.Queries;
using Counterpoint.Ui.ViewModels.Reports;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// AC-17 for the P3-T06 reports: every owner-only query refuses a cashier session and a signed-out one when called
/// straight at the Application service - no screen, no viewmodel - through the real <c>RoleAuthorisation</c>
/// registration, and works for the owner; the concrete queries are not reachable undecorated; and stock on hand and
/// the reorder list stay open to both roles.
/// </summary>
/// <remarks>
/// One fixture (the P3-T05 history plus the P3-T06 stock history) is shared by the class; each test signs in as the
/// role it needs. xunit runs a class's tests one at a time, so the session is never shared between two of them.
/// </remarks>
public sealed class StockCashReportAuthorisationTests(StockCashReportAuthorisationTests.World world)
    : IClassFixture<StockCashReportAuthorisationTests.World>
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static readonly DateTimeOffset Cutoff = StockCashDataset.At(10, 0, 0);

    /// <summary>Every owner-only call in the P3-T06 catalogue, by name, each a direct call on the registered service.</summary>
    private static readonly (string Name, Func<SaleFixture, Task> Call)[] OwnerOnlyCalls =
    [
        ("ITaxReportQuery.GetTaxReportAsync", host => host.Resolve<ITaxReportQuery>().GetTaxReportAsync(BothDays)),
        ("ITenderReconciliationQuery.GetReconciliationAsync", host => host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(BothDays)),
        ("IShiftVarianceHistoryQuery.GetHistoryAsync", host => host.Resolve<IShiftVarianceHistoryQuery>().GetHistoryAsync(BothDays)),
        ("IStockCardQuery.GetStockCardAsync", host => host.Resolve<IStockCardQuery>().GetStockCardAsync(1, BothDays)),
        ("IStockCardQuery.GetStockCardBySkuAsync", host => host.Resolve<IStockCardQuery>().GetStockCardBySkuAsync("SKEL-001-A", BothDays)),
        ("IDamageAdjustmentReportQuery.GetReportAsync", host => host.Resolve<IDamageAdjustmentReportQuery>().GetReportAsync(BothDays)),
        ("ISupplierPurchaseReportQuery.GetReportAsync", host => host.Resolve<ISupplierPurchaseReportQuery>().GetReportAsync(BothDays)),
        ("IFastMovingReportQuery.GetReportAsync", host => host.Resolve<IFastMovingReportQuery>().GetReportAsync(BothDays)),
        ("IStockValuationQuery.GetValuationAsync", host => host.Resolve<IStockValuationQuery>().GetValuationAsync()),
        ("IStockValuationQuery.GetValuationAsync(filter)", host => host.Resolve<IStockValuationQuery>().GetValuationAsync(new StockValuationFilter())),
        ("ISlowMovingStockQuery.FindAsync", host => host.Resolve<ISlowMovingStockQuery>().FindAsync(Cutoff)),
        ("ISlowMovingStockQuery.FindAsync(filter)", host => host.Resolve<ISlowMovingStockQuery>().FindAsync(new SlowMovingFilter(Cutoff))),
        ("IAdjustmentHistoryQuery.ListAsync(FromDate)", host => host.Resolve<IAdjustmentHistoryQuery>().ListAsync(new AdjustmentHistoryFilter(FromDate: new DateOnly(2026, 9, 8)))),
    ];

    private static readonly Type[] OwnerOnlyInterfaces =
    [
        typeof(ITaxReportQuery),
        typeof(ITenderReconciliationQuery),
        typeof(IShiftVarianceHistoryQuery),
        typeof(IStockCardQuery),
        typeof(IDamageAdjustmentReportQuery),
        typeof(ISupplierPurchaseReportQuery),
        typeof(IFastMovingReportQuery),
        typeof(IStockValuationQuery),
        typeof(ISlowMovingStockQuery),
    ];

    private static readonly Type[] ConcreteOwnerOnlyQueries =
    [
        typeof(TaxReportQuery),
        typeof(TenderReconciliationQuery),
        typeof(ShiftVarianceHistoryQuery),
        typeof(StockCardQuery),
        typeof(DamageAdjustmentReportQuery),
        typeof(SupplierPurchaseReportQuery),
        typeof(FastMovingReportQuery),
        typeof(StockValuationQuery),
        typeof(SlowMovingStockQuery),
    ];

    private SaleFixture Host => world.Host;

    /// <summary>SaleFixture.TryResolve is generic; the loops above hold their types as values.</summary>
    private object? TryResolve(Type serviceType) =>
        typeof(SaleFixture).GetMethod(nameof(SaleFixture.TryResolve), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(serviceType).Invoke(Host, null);

    [Fact]
    public async Task AC_17_ACashierSessionIsRefusedByEveryOwnerOnlyStockTaxAndCashReportCalledDirectly()
    {
        await world.SignInAsCashierAsync();
        Host.Resolve<ISession>().Role.Should().Be(Role.Cashier);

        foreach (var (name, call) in OwnerOnlyCalls)
        {
            Func<Task> act = () => call(Host);

            var thrown = await act.Should().ThrowAsync<NotAuthorisedException>("{0} is owner-only", name);
            thrown.Which.Message.Should().Contain("owner", name);
        }
    }

    [Fact]
    public async Task AC_17_ANobodySignedInIsRefusedByEveryOwnerOnlyStockTaxAndCashReportToo()
    {
        await world.SignOutAsync();
        Host.Resolve<ISession>().IsAuthenticated.Should().BeFalse();

        foreach (var (name, call) in OwnerOnlyCalls)
        {
            Func<Task> act = () => call(Host);

            await act.Should().ThrowAsync<NotAuthorisedException>("{0} needs a signed-in owner", name);
        }
    }

    [Fact]
    public async Task AC_17_TheOwnerSessionGetsTheRealFiguresFromEveryOneOfThem()
    {
        // The control for the refusals above: the guard is a permission check, not a wall.
        await world.SignInAsOwnerAsync();

        foreach (var (name, call) in OwnerOnlyCalls)
        {
            Func<Task> act = () => call(Host);

            await act.Should().NotThrowAsync(name);
        }

        (await Host.Resolve<ITaxReportQuery>().GetTaxReportAsync(BothDays)).NetTax.Amount.Should().Be(85.85m);
        (await Host.Resolve<IStockValuationQuery>().GetValuationAsync()).TotalValue.Amount.Should().Be(223051.02037312m);
        (await Host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(BothDays)).RangeNetTotal.Amount.Should().Be(1244.35m);
    }

    [Fact]
    public async Task AC_17_StockOnHandTheReorderListAndTheFilterLookupStayOpenToACashier()
    {
        await world.SignInAsCashierAsync();

        var onHand = await Host.Resolve<IStockOnHandQuery>().GetStockOnHandAsync(new StockOnHandFilter());
        onHand.Lines.Should().HaveCount(9);

        (await Host.Resolve<IReorderListQuery>().GetReorderListAsync()).Should().HaveCount(5);
        (await Host.Resolve<IReorderListQuery>().GetReorderListBySupplierAsync(new ReorderListFilter())).Should().HaveCount(3);

        (await Host.Resolve<IReportFilterLookup>().ListCategoriesAsync()).Select(option => option.Name)
            .Should().Contain(["Fasteners", "Tools", "Fasteners / Machine screws"]);
        (await Host.Resolve<IReportFilterLookup>().ListSuppliersAsync()).Select(option => option.Name)
            .Should().Equal("Acme Fasteners", "Zenith Tools");
        (await Host.Resolve<IReportFilterLookup>().ListBrandsAsync()).Select(option => option.Name)
            .Should().Equal("Bosch", "Makita");
    }

    [Fact]
    public void AC_17_EveryOwnerOnlyReportInterfaceDeclaresTheOwnerRoleAndTheCostFreeOnesDeclareNone()
    {
        foreach (var contract in OwnerOnlyInterfaces)
        {
            var attribute = contract.GetCustomAttribute<RequiresRoleAttribute>();

            attribute.Should().NotBeNull("{0} is owner-only", contract.Name);
            attribute!.Role.Should().Be(Role.Owner, contract.Name);
        }

        typeof(IStockOnHandQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
        typeof(IReorderListQuery).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
        typeof(IReportFilterLookup).GetCustomAttribute<RequiresRoleAttribute>().Should().BeNull();
    }

    [Fact]
    public void AC_17_TheConcreteOwnerOnlyQueriesAreNotResolvableUndecoratedAndAreNotPublic()
    {
        foreach (var concrete in ConcreteOwnerOnlyQueries)
        {
            concrete.IsVisible.Should().BeFalse("{0} must be internal so only the decorated interface reaches it", concrete.Name);

            var resolved = TryResolve(concrete);
            resolved.Should().BeNull("there is no bare registration of {0}", concrete.Name);
        }

        foreach (var contract in OwnerOnlyInterfaces)
        {
            var service = TryResolve(contract)!;
            service.Should().NotBeNull(contract.Name);
            ConcreteOwnerOnlyQueries.Should().NotContain(service.GetType(), "{0} resolves to the role-authorisation proxy", contract.Name);
        }

        // And no class in the Reporting assembly that implements a [RequiresRole] interface is visible outside it.
        var exposed = typeof(TaxReportQuery).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsVisible)
            .Where(type => type.GetInterfaces().Any(contract => contract.IsDefined(typeof(RequiresRoleAttribute), inherit: true)))
            .Select(type => type.FullName)
            .ToList();

        exposed.Should().BeEmpty("a public owner-only implementation could be constructed with no role check in front of it");
    }

    [Fact]
    public async Task AC_17_EveryOwnerOnlyScreenShowsACashierAPlainSentenceAndNoFiguresNotAnExceptionOrATypeName()
    {
        await world.SignInAsCashierAsync();
        var clock = new FixedTimeProvider(StockCashDataset.At(10, 12, 0));
        var filters = Host.Resolve<IReportFilterLookup>();

        var tax = new TaxReportViewModel(Host.Resolve<ITaxReportQuery>(), clock);
        var tender = new TenderReconciliationViewModel(Host.Resolve<ITenderReconciliationQuery>(), clock);
        var variance = new ShiftVarianceViewModel(Host.Resolve<IShiftVarianceHistoryQuery>(), clock);
        var card = new StockCardViewModel(Host.Resolve<IStockCardQuery>(), clock) { SkuText = "SKEL-001-A" };
        var damage = new DamageAdjustmentViewModel(Host.Resolve<IDamageAdjustmentReportQuery>(), clock);
        var purchases = new SupplierPurchasesViewModel(Host.Resolve<ISupplierPurchaseReportQuery>(), filters, clock);
        var fast = new FastMovingViewModel(Host.Resolve<IFastMovingReportQuery>(), clock);
        var valuation = new StockValuationViewModel(Host.Resolve<IStockValuationQuery>(), filters, clock);
        var slow = new SlowMovingStockViewModel(Host.Resolve<ISlowMovingStockQuery>(), filters, clock);

        await tax.RunCommand.ExecuteAsync(null);
        await tender.RunCommand.ExecuteAsync(null);
        await variance.RunCommand.ExecuteAsync(null);
        await card.RunCommand.ExecuteAsync(null);
        await damage.RunCommand.ExecuteAsync(null);
        await purchases.RunCommand.ExecuteAsync(null);
        await fast.RunCommand.ExecuteAsync(null);
        await valuation.RunCommand.ExecuteAsync(null);
        await slow.RunCommand.ExecuteAsync(null);

        var screens = new (string Name, ReportScreenViewModelBase Screen, ReportTableViewModel[] Tables)[]
        {
            ("tax", tax, [tax.Rates]),
            ("tender reconciliation", tender, [tender.ByTender, tender.Shifts, tender.NotZd]),
            ("shift and variance history", variance, [variance.Shifts]),
            ("stock card", card, [card.Movements]),
            ("damage and adjustments", damage, [damage.Rows]),
            ("supplier purchases", purchases, [purchases.BySupplier, purchases.ByItem, purchases.CostMovement]),
            ("fast-moving items", fast, [fast.ByUnits, fast.ByValue]),
            ("stock valuation", valuation, [valuation.Lines]),
            ("slow-moving stock", slow, [slow.Lines]),
        };

        foreach (var (name, screen, tables) in screens)
        {
            screen.HasStatus.Should().BeTrue("{0} must tell the cashier why there is nothing", name);
            screen.Status.Should().Be("The " + name + " report is for the owner. Sign in as the owner to see it.");
            screen.Status.Should().NotContainEquivalentOf("Exception").And.NotContain("Counterpoint").And.NotContain("NotAuthorised");
            tables.Should().OnlyContain(table => table.Rows.Count == 0, "{0} shows no figures to a cashier", name);
            screen.Busy.Should().BeFalse();
        }
    }

    /// <summary>The P3-T05 + P3-T06 history, plus a cashier, built once for the class.</summary>
    public sealed class World : IAsyncLifetime
    {
        internal SaleFixture Host { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
            await StockCashDataset.BuildAsync(Host);
            await Host.Resolve<IUserAdministration>().CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();

        internal async Task SignOutAsync() => await Host.Resolve<IAuthenticationService>().LogOutAsync();

        internal async Task SignInAsCashierAsync()
        {
            await SignOutAsync();
            (await Host.Resolve<IAuthenticationService>().LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();
        }

        internal async Task SignInAsOwnerAsync()
        {
            await SignOutAsync();
            (await Host.Resolve<IAuthenticationService>().LogInAsync(SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword))
                .Succeeded.Should().BeTrue();
        }
    }
}
