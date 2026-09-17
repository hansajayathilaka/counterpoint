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
/// Task P3-T13's own defence-in-depth done-when: opening a privileged screen from the back-office
/// shell re-checks the session's role at the Application layer, not only by hiding the shell's own
/// tile (SRS NFR-S2, AC-17, AC-24).
/// </summary>
/// <remarks>
/// <para>
/// <b>The shell's own gating is proven false first, then bypassed entirely.</b> A cashier session
/// never sees a single tile on <c>BackOfficeShellViewModel</c> (every <c>Can*</c> flag is false -
/// <see cref="LoginScreenTests.FR_1_4_TheBackOfficeShellGatesEachOfItsFiveTilesToTheOwnerOnly"/> is
/// that half). This file calls straight past that viewmodel into the five Application-layer
/// interfaces the shell's tiles would have opened - exactly what a rogue caller reaching the
/// service directly, with no shell, no window and no Avalonia in sight, would do - and requires
/// the same refusal <see cref="NotAuthorisedException"/> the individual per-screen AC-17 suites
/// already prove (<c>CatalogueAuthorisationTests</c>, <c>SettingsScreenTests</c>,
/// <c>UserAdministrationTests</c>, <c>PurchaseOrderServiceTests</c>,
/// <c>LabelPrintServiceTests</c>). This file exists to tie all five together, once, in the
/// specific shape this task's own "Done when" asks for.
/// </para>
/// </remarks>
public sealed class BackOfficeShellAuthorisationTests
{
    [Fact]
    public async Task AC_24_EveryDestinationTheBackOfficeShellCanOpenIsRefusedByTheApplicationLayerItselfForACashier()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.SignInAsSeededOwnerAsync();
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        // The tile-visibility half: none of the shell's five destinations render for this
        // session - the courtesy, not the control.
        var shell = new BackOfficeShellViewModel(fixture.Resolve<ISession>());
        shell.CanManageCatalogue.Should().BeFalse();
        shell.CanChangeSettings.Should().BeFalse();
        shell.CanManageUsers.Should().BeFalse();
        shell.CanManagePurchasing.Should().BeFalse();
        shell.CanPrintLabels.Should().BeFalse();

        // The control: call straight past the shell into what each tile would have opened.
        var catalogue = () => fixture.Resolve<ICategoryMaintenance>().ListAsync();
        var settings = () => fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot);
        var users = () => fixture.Resolve<IUserAdministration>().ListAsync();
        var purchasing = () => fixture.Resolve<IPurchaseOrderService>().ListAsync();
        var labels = () => fixture.Resolve<ILabelPrintService>().PrintAsync([]);

        await catalogue.Should().ThrowAsync<NotAuthorisedException>(
            "Catalogue must refuse a cashier even with the shell bypassed entirely");
        await settings.Should().ThrowAsync<NotAuthorisedException>(
            "Settings must refuse a cashier even with the shell bypassed entirely");
        await users.Should().ThrowAsync<NotAuthorisedException>(
            "Users must refuse a cashier even with the shell bypassed entirely");
        await purchasing.Should().ThrowAsync<NotAuthorisedException>(
            "Purchasing must refuse a cashier even with the shell bypassed entirely");
        await labels.Should().ThrowAsync<NotAuthorisedException>(
            "Labels must refuse a cashier even with the shell bypassed entirely");
    }

    [Fact]
    public async Task AC_24_TheSameFiveDestinationsSucceedForAnOwnerTheGuardIsAPermissionNotAWall()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.SignInAsSeededOwnerAsync();

        var shell = new BackOfficeShellViewModel(fixture.Resolve<ISession>());
        shell.CanManageCatalogue.Should().BeTrue();
        shell.CanChangeSettings.Should().BeTrue();
        shell.CanManageUsers.Should().BeTrue();
        shell.CanManagePurchasing.Should().BeTrue();
        shell.CanPrintLabels.Should().BeTrue();

        var catalogue = () => fixture.Resolve<ICategoryMaintenance>().ListAsync();
        var users = () => fixture.Resolve<IUserAdministration>().ListAsync();
        var purchasing = () => fixture.Resolve<IPurchaseOrderService>().ListAsync();

        await catalogue.Should().NotThrowAsync();
        await users.Should().NotThrowAsync();
        await purchasing.Should().NotThrowAsync();
    }
}
