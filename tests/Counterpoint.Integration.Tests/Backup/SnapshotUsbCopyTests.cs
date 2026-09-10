using System;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Backup.Snapshots;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// P1-T15: <see cref="SnapshotService.CreateSnapshotAsync"/>'s USB copy (SRS FR-11.3), over a
/// real backup file and a real (or deliberately absent) folder on disk - never a mock file
/// system, because "the folder is not there" is exactly the failure mode this task has to prove
/// degrades to a warning rather than a lost backup.
/// </summary>
public sealed class SnapshotUsbCopyTests
{
    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public async Task FR11_3_AUsbFolderThatIsPresentGetsACopyOfTheFinishedBackup()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var usbDirectory = Path.Combine(Path.GetTempPath(), "counterpoint-usb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(usbDirectory);

        try
        {
            var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync(usbDirectory);

            snapshot.UsbStatus.Should().Be("OK");
            snapshot.UsbWarning.Should().BeNull();

            var usbCopyPath = Path.Combine(usbDirectory, snapshot.Filename);
            File.Exists(usbCopyPath).Should().BeTrue("the finished, encrypted file must have been copied onto the USB folder");

            File.ReadAllBytes(usbCopyPath).Should().Equal(
                File.ReadAllBytes(snapshot.FilePath),
                "the USB copy must be byte-for-byte the same file that was already safely on the local disk, "
                    + "not a second, independently produced backup");

            var row = await fixture.ScalarAsync(
                "SELECT usb_status FROM backup_record ORDER BY id DESC LIMIT 1;");
            row.Should().Be("OK");
        }
        finally
        {
            Directory.Delete(usbDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Done-when item 4: pointing the USB path at a missing location produces a warning and the
    /// local backup still succeeds. A real USB stick pulled mid-write is HW-T08, not this test.
    /// </summary>
    [Fact]
    public async Task FR11_3_AMissingUsbFolderWarnsButTheLocalBackupStillSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var missingUsbDirectory = Path.Combine(
            Path.GetTempPath(), "counterpoint-usb-tests", "does-not-exist-" + Guid.NewGuid().ToString("N"));

        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync(missingUsbDirectory);

        snapshot.UsbStatus.Should().Be("FAILED", "the configured USB folder does not exist");
        snapshot.UsbWarning.Should().NotBeNullOrWhiteSpace("a plain-language reason must be available to show the owner");

        File.Exists(snapshot.FilePath).Should().BeTrue(
            "the local backup must not be undone by a USB problem (CLAUDE.md invariant 7, SRS FR-11.3)");

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            1, "the local backup must still be recorded even though the USB copy failed");

        var row = await fixture.ScalarAsync(
            "SELECT usb_status || '|' || (last_error IS NOT NULL) FROM backup_record ORDER BY id DESC LIMIT 1;");
        row.Should().Be("FAILED|1");
    }

    /// <summary>
    /// Regression guard for the P0-T07 behaviour <c>BackupRoundTripTests</c> already locks in via
    /// the default parameter: an explicit null USB directory - "no USB configured at all", as
    /// opposed to "configured but missing" above - must still read <c>NA</c>, not <c>FAILED</c>.
    /// </summary>
    [Fact]
    public async Task FR11_3_NoUsbDirectoryConfiguredAtAllLeavesUsbStatusNa()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync(usbDirectory: null);

        snapshot.UsbStatus.Should().Be("NA", "no USB path is configured, which is not the same fault as one that is configured but absent");
        snapshot.UsbWarning.Should().BeNull();
    }
}
