using System;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Labels;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Security;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// <b>AC-24</b> — "A cashier-role session and an owner-role session, on the same install and the
/// same database, present visually and navigationally distinct shells (UI-11), with no path from
/// the cashier shell to an owner-only screen that bypasses the existing Application-layer role
/// check." Task P3-T16's closing gate for the whole UI redesign.
/// </summary>
/// <remarks>
/// Task P3-T13 already proves every one of the three claims AC-24 makes, piecemeal, across three
/// files: <c>BackOfficeShellTests</c> (visual distinctness, by file inspection - a window cannot
/// be opened in CI), <c>LoginScreenTests.AC_24_TheOwnerSeesTheBackOfficeEntryPointAndTheCashierDoesNot</c>/
/// <c>FR_1_4_TheBackOfficeShellGatesEachOfItsFiveTilesToTheOwnerOnly</c> (navigational
/// distinctness, from the sales screen and inside the shell) and
/// <c>BackOfficeShellAuthorisationTests</c> (the Application-layer control itself, with the shell
/// bypassed entirely). This class does not repeat their assertions field-by-field; it states the
/// one combined AC-24 claim once, over one shared session per role, as its own named acceptance
/// gate.
/// </remarks>
public sealed class AC24_CashierAndOwnerShellsAreVisuallyAndNavigationallyDistinct
{
    private const string SolutionFileName = "Counterpoint.sln";

    [Fact]
    public void AC_24_SalesWindowAndBackOfficeShellWindowAreVisuallyDistinctMarkup()
    {
        var salesWindow = ReadUiFile("Views", "SalesWindow.axaml");
        var backOfficeShell = ReadUiFile("Views", "BackOfficeShellWindow.axaml");

        // Neither window's chrome is the other's (SRS UI-11): the back office names itself and
        // carries its own accent stripe, which the sales screen has neither of.
        backOfficeShell.Should().Contain("BACK OFFICE");
        salesWindow.Should().NotContain("BACK OFFICE");

        backOfficeShell.Should().Contain("{DynamicResource AccentBrush}");

        // Cashier and owner shells still resolve through the one Light/Dark token system (SRS
        // UI-13) - AC-24 is a distinct chrome inside one theme, never a second theme system.
        salesWindow.Should().NotContain("RequestedThemeVariant");
        backOfficeShell.Should().NotContain("RequestedThemeVariant");
    }

    [Fact]
    public async Task AC_24_OneSessionSwitchesWhichShellIsNavigableWithNoSecondLoginOrConnectionAsync()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SignInAsSeededOwnerAsync();
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var session = fixture.Resolve<ISession>();
        var authentication = fixture.Resolve<IAuthenticationService>();

        // The owner's session: the sales screen's own entry point renders, and every one of the
        // shell's five tiles renders too.
        var sales = NewSales(fixture);
        var shell = new BackOfficeShellViewModel(session);

        sales.CanOpenBackOffice.Should().BeTrue();
        shell.CanManageCatalogue.Should().BeTrue();
        shell.CanChangeSettings.Should().BeTrue();
        shell.CanManageUsers.Should().BeTrue();
        shell.CanManagePurchasing.Should().BeTrue();
        shell.CanPrintLabels.Should().BeTrue();

        // The same instances, the same ISession singleton, no second login and no second
        // connection - just a different signed-in user (SRS AC-24, task P3-T13's own claim).
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        sales.CanOpenBackOffice.Should().BeFalse(
            "a cashier session must never see the one path into the back office");
        shell.CanManageCatalogue.Should().BeFalse();
        shell.CanChangeSettings.Should().BeFalse();
        shell.CanManageUsers.Should().BeFalse();
        shell.CanManagePurchasing.Should().BeFalse();
        shell.CanPrintLabels.Should().BeFalse();
    }

    [Fact]
    public async Task AC_24_NoTileHidingTrickReachesAnOwnerOnlyScreenForACashierAsync()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SignInAsSeededOwnerAsync();
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        await fixture.Resolve<IAuthenticationService>().LogOutAsync();
        await fixture.Resolve<IAuthenticationService>().LogInAsync("priya", "counter1");

        // The shell would have hidden every tile (proven above); this calls straight past it into
        // what each tile would have opened, exactly as a rogue caller with no shell, no window and
        // no Avalonia in sight would - the control, not the courtesy.
        var catalogue = () => fixture.Resolve<ICategoryMaintenance>().ListAsync();
        var settings = () => fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot);
        var users = () => fixture.Resolve<IUserAdministration>().ListAsync();
        var purchasing = () => fixture.Resolve<IPurchaseOrderService>().ListAsync();
        var labels = () => fixture.Resolve<ILabelPrintService>().PrintAsync([]);

        await catalogue.Should().ThrowAsync<NotAuthorisedException>();
        await settings.Should().ThrowAsync<NotAuthorisedException>();
        await users.Should().ThrowAsync<NotAuthorisedException>();
        await purchasing.Should().ThrowAsync<NotAuthorisedException>();
        await labels.Should().ThrowAsync<NotAuthorisedException>();
    }

    private static SalesViewModel NewSales(SaleFixture fixture) => new(
        fixture.Resolve<Counterpoint.Application.Sales.IScanItem>(),
        fixture.Resolve<Counterpoint.Application.Sales.IQuoteSale>(),
        fixture.Resolve<Counterpoint.Application.Sales.ICompleteSale>(),
        fixture.Resolve<Counterpoint.Application.Abstractions.Persistence.ITillSessionProvider>(),
        fixture.Resolve<ISession>(),
        fixture.Resolve<ISettings>(),
        fixture.Resolve<Counterpoint.Application.Abstractions.Persistence.IProductSearchService>(),
        fixture.Resolve<Counterpoint.Application.Abstractions.Persistence.ICustomerStore>(),
        fixture.Resolve<Counterpoint.Application.Abstractions.Persistence.IUomStore>(),
        fixture.Resolve<Counterpoint.Application.Inventory.IStockEnquiry>(),
        fixture.Resolve<Counterpoint.Application.Sales.IHeldBillService>(),
        fixture.Resolve<Counterpoint.Application.Shifts.IOpenShift>(),
        fixture.Resolve<Counterpoint.Application.Dashboard.IDashboardQueries>(),
        fixture.Resolve<Counterpoint.Application.Sales.IReprintReceipt>(),
        fixture.Resolve<Counterpoint.Application.Abstractions.Persistence.IPrintJobOutbox>(),
        fixture.Resolve<TimeProvider>());

    private static string ReadUiFile(params string[] relativeSegments)
    {
        var path = Path.Combine(
            RepositoryRoot().FullName,
            "src",
            "Counterpoint.Ui",
            Path.Combine(relativeSegments));

        File.Exists(path).Should().BeTrue("expected {0} to exist", path);

        return File.ReadAllText(path);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
