using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// P1-T14's screen-level proofs: a sale cannot be made without an open shift, the cashier can
/// open one from the sales screen and see the status bar and F9 Pay unblock immediately, and the
/// compact dashboard shows today's figures (SRS FR-8.1, FR-8.7, FR-9.7, UI-09).
/// </summary>
public sealed class ShiftOpenAndDashboardScreenTests
{
    [Fact]
    public async Task FR_8_1_ASaleCannotBeMadeWithoutAnOpenShift()
    {
        // No shift open before anyone signs in - the morning scenario FR-8.1 and FR-8.7 describe,
        // not a shift closing out from under an already-trading session (nothing in Phase 1 can
        // do that; shift close is P3-T01's).
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        await fixture.SignInAsSeededOwnerAsync();

        var screen = BuildScreen(fixture);

        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);
        screen.Lines.Should().ContainSingle("scanning an item needs no open shift, only paying for it does");

        screen.PayCommand.Execute(null);
        await screen.CompletePaymentCommand.ExecuteAsync(null);

        screen.Status.Should().Be("There is no open shift. Open one before trading.");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(0, "the refused attempt must not have written a bill");
    }

    [Fact]
    public async Task FR_8_1_TheCashierCanOpenAShiftFromTheSalesScreenAndTheStatusBarUpdatesImmediately()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        await fixture.SignInAsSeededOwnerAsync();

        var screen = BuildScreen(fixture);

        screen.CanOpenShift.Should().BeTrue();
        screen.StatusShiftText.Should().Be("No shift open");

        screen.OpenShiftCommand.Execute(null);
        screen.IsOpenShiftPanelOpen.Should().BeTrue();

        screen.OpeningFloatText = "5000";
        await screen.ConfirmOpenShiftCommand.ExecuteAsync(null);

        screen.IsOpenShiftPanelOpen.Should().BeFalse("opening the shift closes the panel");
        screen.CanOpenShift.Should().BeFalse("the button is only shown while no shift is open");
        screen.StatusShiftText.Should().StartWith("Shift #");
        screen.Status.Should().StartWith("Opened shift SH-");

        // And the freshly opened shift actually works: a sale now completes.
        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);
        screen.PayCommand.Execute(null);
        await screen.CompletePaymentCommand.ExecuteAsync(null);

        screen.Status.Should().StartWith("Saved as INV-");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(1);
    }

    [Fact]
    public async Task FR_9_7_TheDashboardPanelShowsTodaysFigures()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var screen = BuildScreen(fixture);

        screen.Barcode = FirstRunSeeder.SeededBarcode;
        await screen.ScanCommand.ExecuteAsync(null);
        screen.PayCommand.Execute(null);
        await screen.CompletePaymentCommand.ExecuteAsync(null);

        screen.IsDashboardPanelOpen.Should().BeFalse();
        await screen.DashboardCommand.ExecuteAsync(null);

        screen.IsDashboardPanelOpen.Should().BeTrue();
        screen.DashboardText.Should().Contain("Bills: 1");
        screen.DashboardText.Should().Contain("Today's sales: 12.50");
        screen.DashboardText.Should().Contain("Last backup: none yet");
    }

    [Fact]
    public async Task NFR_DashboardRefreshRunsIndependentlyOfTheScanPathAndDoesNotBlockAScan()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var screen = BuildScreen(fixture);
        await screen.DashboardCommand.ExecuteAsync(null);
        screen.IsDashboardPanelOpen.Should().BeTrue();

        // A dashboard refresh (a read-only query on a read connection) running at the same moment
        // as a scan (which never touches the write connection either) must not block or corrupt
        // either one - the timer in SalesWindow's code-behind and the scan box are two
        // independent paths by construction (P1-T14 "Risks").
        screen.Barcode = FirstRunSeeder.SeededBarcode;
        var refreshTask = screen.RefreshDashboardCommand.ExecuteAsync(null);
        var scanTask = screen.ScanCommand.ExecuteAsync(null);

        await Task.WhenAll(refreshTask, scanTask);

        screen.Lines.Should().ContainSingle();
        screen.DashboardText.Should().Contain("Bills: 0", "no bill has been paid for yet");
    }

    [Fact]
    public async Task FR_8_7_ClosingTheAppWithAnOpenShiftWarns()
    {
        // Recovering an open shift on restart is the other half of FR-8.7, proved in
        // ShiftRecoveryTests; this is the warning half, read by SalesWindow's Closing handler.
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var screen = BuildScreen(fixture);

        screen.ShutdownWarning.Should().NotBeNull("a shift is open, and closing now must warn (FR-8.7)");
        screen.ShutdownWarning.Should().Contain("shift").And.Contain("Closing now is fine");
    }

    [Fact]
    public async Task FR_8_7_ClosingTheAppWithNoShiftOpenDoesNotWarn()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        await fixture.SignInAsSeededOwnerAsync();

        var screen = BuildScreen(fixture);

        screen.ShutdownWarning.Should().BeNull("there is nothing open to warn about");
    }

    private static SalesViewModel BuildScreen(SaleFixture fixture) => new(
        fixture.Resolve<IScanItem>(),
        fixture.Resolve<IQuoteSale>(),
        fixture.Resolve<ICompleteSale>(),
        fixture.Resolve<ITillSessionProvider>(),
        fixture.Resolve<ISession>(),
        fixture.Resolve<ISettings>(),
        fixture.Resolve<IProductSearchService>(),
        fixture.Resolve<ICustomerStore>(),
        fixture.Resolve<IUomStore>(),
        fixture.Resolve<IStockEnquiry>(),
        fixture.Resolve<IHeldBillService>(),
        fixture.Resolve<IOpenShift>(),
        fixture.Resolve<IDashboardQueries>(),
        fixture.Resolve<TimeProvider>());
}
