using System;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Backup.Orchestration;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// P1-T15 done-when item 1: a scheduled backup runs unattended and appears in
/// <c>backup_record</c> (SRS FR-11.1), over a real, migrated SQLite database - never the
/// in-memory provider. <see cref="BackupScheduler.TickAsync"/> is driven directly, one tick at a
/// time, the same shape <c>Counterpoint.Devices.Printing.PrintWorker.DrainAsync</c> already uses
/// in this suite: it is the exact method the scheduler's own <c>BackgroundService</c> loop calls
/// on a timer, so there is nothing left for that loop itself to prove that a real poll interval
/// would not just make slower.
/// </summary>
public sealed class BackupSchedulerTests
{
    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public async Task FR11_1_AScheduledBackupRunsUnattendedAndAppearsInBackupRecord()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        // The fixture's clock is fixed at 09:15 local (FixedTimeProvider.LocalTimeZone is UTC) -
        // a daily time of 08:00 is already due on the very first tick.
        await SetDailyTimeAsync(fixture, new TimeOnly(8, 0));

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(0);

        await fixture.Resolve<BackupScheduler>().TickAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            1, "a scheduled backup due right now must run unattended and record itself (SRS FR-11.1)");

        var row = await fixture.ScalarAsync(
            "SELECT usb_status || '|' || cloud_status FROM backup_record ORDER BY id DESC LIMIT 1;");
        row.Should().Be("NA|SKIPPED", "no USB path is configured for this test and cloud stays SKIPPED until Phase 4");
    }

    [Fact]
    public async Task FR11_1_ASecondTickTheSameDayDoesNotTakeASecondBackup()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);
        await SetDailyTimeAsync(fixture, new TimeOnly(8, 0));

        var scheduler = fixture.Resolve<BackupScheduler>();
        await scheduler.TickAsync();
        await scheduler.TickAsync();
        await scheduler.TickAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            1, "one backup already satisfies 'at least once per day' - the scheduler must not take a second one "
                + "just because it happens to check again before midnight (SRS FR-11.1)");
    }

    [Fact]
    public async Task FR11_1_ATickBeforeTheDailyTimeTakesNoBackup()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        // The fixture's clock is fixed at 09:15 local - a daily time later than that is not due yet.
        await SetDailyTimeAsync(fixture, new TimeOnly(23, 59));

        await fixture.Resolve<BackupScheduler>().TickAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            0, "the scheduled time has not arrived yet, so nothing should have run");
    }

    [Fact]
    public async Task FR11_1_ATillThatWasOffAtTheScheduledTimeCatchesUpTheNextDayItStarts()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);
        await SetDailyTimeAsync(fixture, new TimeOnly(8, 0));

        var scheduler = fixture.Resolve<BackupScheduler>();
        await scheduler.TickAsync();
        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(1);

        // The till is switched off overnight and started again the next day, already well past
        // the configured time - this must not wait for tomorrow again, it must catch up now.
        var clock = (FixedTimeProvider)fixture.Resolve<TimeProvider>();
        clock.Advance(TimeSpan.FromDays(1));

        await scheduler.TickAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            2, "nothing has run yet for the new calendar day, so the first tick after start-up must catch up "
                + "immediately rather than silently skipping the day (SRS FR-11.1)");
    }

    private static Task<SettingsSnapshot> SetDailyTimeAsync(SaleFixture fixture, TimeOnly time) =>
        fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot with
        {
            Backup = snapshot.Backup with { DailyBackupTime = time },
        });
}
