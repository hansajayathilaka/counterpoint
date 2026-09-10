using System;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Backup.Snapshots;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// P1-T15 done-when item 5: the dashboard and status bar warn after the configured number of days
/// without a backup, escalating to urgent at twice that limit (SRS FR-11.7's local half). Driven
/// against a real backup file and a real <c>backup_record</c> row - the fixture's own
/// <see cref="FixedTimeProvider"/> clock is what moves time forward, exactly the way
/// <c>SalesViewModel</c> reads it, rather than reaching into its private text-building method.
/// </summary>
public sealed class BackupDashboardWarningTests
{
    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public async Task FR11_7_NoWarningBeforeTheConfiguredNumberOfDaysHasPassed()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);
        await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        // Default backup.warn_after_days is 2 (SettingDefaults) - one day on must still be silent.
        var clock = (FixedTimeProvider)fixture.Resolve<TimeProvider>();
        clock.Advance(TimeSpan.FromDays(1));

        var screen = BuildScreen(fixture);
        await screen.RefreshDashboardCommand.ExecuteAsync(null);

        screen.StatusBackupText.Should().NotContain("WARNING").And.NotContain("URGENT");

        await screen.DashboardCommand.ExecuteAsync(null);
        screen.DashboardText.Should().Contain("Last backup:").And.NotContain("WARNING").And.NotContain("URGENT");
    }

    [Fact]
    public async Task FR11_7_TheDashboardWarnsOnceTheConfiguredNumberOfDaysIsReachedButNotYetUrgent()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);
        await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        // Default backup.warn_after_days is 2 (SettingDefaults).
        var clock = (FixedTimeProvider)fixture.Resolve<TimeProvider>();
        clock.Advance(TimeSpan.FromDays(2));

        var screen = BuildScreen(fixture);
        await screen.RefreshDashboardCommand.ExecuteAsync(null);

        screen.StatusBackupText.Should().Contain("WARNING").And.NotContain("URGENT");

        await screen.DashboardCommand.ExecuteAsync(null);
        screen.DashboardText.Should().Contain("WARNING").And.NotContain("URGENT");
    }

    [Fact]
    public async Task FR11_7_TheDashboardEscalatesToUrgentAtTwiceTheConfiguredLimit()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);
        await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        var clock = (FixedTimeProvider)fixture.Resolve<TimeProvider>();
        clock.Advance(TimeSpan.FromDays(4)); // 2x the default 2-day limit

        var screen = BuildScreen(fixture);
        await screen.RefreshDashboardCommand.ExecuteAsync(null);

        screen.StatusBackupText.Should().Contain("URGENT");

        await screen.DashboardCommand.ExecuteAsync(null);
        screen.DashboardText.Should().Contain("URGENT");
    }

    [Fact]
    public async Task FR11_7_TheWarningThresholdIsTheShopsOwnConfiguredValueNotAHardcodedDefault()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);
        await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        // Tighten the shop's own tolerance to 1 day - the default (2) would otherwise mask this.
        await fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot with
        {
            Backup = snapshot.Backup with { WarnAfterDays = 1 },
        });

        var clock = (FixedTimeProvider)fixture.Resolve<TimeProvider>();
        clock.Advance(TimeSpan.FromDays(1));

        var screen = BuildScreen(fixture);
        await screen.RefreshDashboardCommand.ExecuteAsync(null);

        screen.StatusBackupText.Should().Contain(
            "WARNING", "the configured 1-day limit has been reached, even though the default of 2 days has not");
    }

    [Fact]
    public async Task FR11_7_ATillThatHasNeverTakenABackupWarnsImmediately()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var screen = BuildScreen(fixture);
        await screen.RefreshDashboardCommand.ExecuteAsync(null);

        screen.StatusBackupText.Should().Contain("WARNING").And.Contain("no backup has ever been taken");
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
        fixture.Resolve<IReprintReceipt>(),
        fixture.Resolve<IPrintJobOutbox>(),
        fixture.Resolve<TimeProvider>());
}
