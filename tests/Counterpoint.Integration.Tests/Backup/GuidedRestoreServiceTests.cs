using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Sales;
using Counterpoint.Backup.Restore;
using Counterpoint.Backup.Snapshots;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// P1-T15 done-when item 3 and the guided restore wizard's own gates (SRS FR-11.12, FR-11.13),
/// over a real, migrated, SQLCipher-encrypted database and real backup files on disk - never the
/// in-memory provider.
/// </summary>
public sealed class GuidedRestoreServiceTests
{
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public async Task FR11_12_RestoringFromTheUsbCopyReproducesTheDatabaseExactlyWithTheCurrentDatabaseBackedUpFirst()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var firstSale = await CompleteOneAsync(fixture);

        var usbDirectory = Path.Combine(Path.GetTempPath(), "counterpoint-usb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(usbDirectory);

        try
        {
            var usbSnapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync(usbDirectory);
            usbSnapshot.UsbStatus.Should().Be("OK", "the setup itself must have actually reached the USB folder");

            // The live database keeps trading after the USB backup was taken - a second sale that
            // must NOT be in the restored data below.
            var secondSale = await CompleteOneAsync(fixture, quantity: 3m);

            (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(2);
            (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(1);
            (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = '"
                + BackupAuditActions.DatabaseRestored + "';")).Should().Be(0);

            var usbCopyPath = Path.Combine(usbDirectory, usbSnapshot.Filename);

            var outcome = await fixture.Resolve<GuidedRestoreService>().RestoreAsync(
                new GuidedRestoreRequest(usbCopyPath, Passphrase, GuidedRestoreConfirmation.RequiredPhrase));

            outcome.RestoredSchemaVersion.Should().Be(usbSnapshot.SchemaVersion);
            outcome.RestoredDataDate.Should().Be(usbSnapshot.TakenAt);
            outcome.SafetyBackupFilename.Should().NotBeNullOrWhiteSpace();
            outcome.RequiresRestart.Should().BeTrue();

            // (b) a safety backup of the CURRENT (post-second-sale) database was taken first.
            (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
                2, "the current database must be backed up before anything is restored (SRS FR-11.12)");

            var safetyRow = await fixture.ScalarAsync(
                "SELECT filename || '|' || usb_status FROM backup_record ORDER BY id DESC LIMIT 1;");
            safetyRow.Should().Be(
                outcome.SafetyBackupFilename + "|NA",
                "the safety backup is a separate, purely local snapshot - it does not itself go to USB");

            // (a) the staged output matches the ORIGINAL data - the first sale only, not the second.
            var stagedPath = PendingRestoreLocation.StagingFilePath(fixture.SnapshotDirectory);
            File.Exists(stagedPath).Should().BeTrue("the decrypted restore must be staged for the next start-up");

            var key = fixture.Resolve<IDatabaseKeyStore>().GetOrCreateKey();

            var stagedSaleCount = await ScalarFromRawFileAsync(stagedPath, key, "SELECT COUNT(*) FROM sale;");
            stagedSaleCount.Should().Be("1", "the staged database must be exactly what the USB backup held, before the second sale");

            var stagedBillNo = await ScalarFromRawFileAsync(
                stagedPath, key, "SELECT bill_no FROM sale WHERE id = " + firstSale.SaleId + ";");
            stagedBillNo.Should().Be(firstSale.BillNo, "the restored row must be the exact first sale, unchanged");

            // (c) the audit log recorded the restore.
            (await fixture.CountAsync(
                "SELECT COUNT(*) FROM audit_log WHERE action = '" + BackupAuditActions.DatabaseRestored + "';"))
                .Should().Be(1);

            var auditReason = await fixture.ScalarAsync(
                "SELECT reason FROM audit_log WHERE action = '" + BackupAuditActions.DatabaseRestored + "';");
            auditReason.Should().Contain(usbSnapshot.Filename).And.Contain(outcome.SafetyBackupFilename);

            // Never touches the live database file directly - a single-instance till has no safe
            // way to swap it out from under itself while running (CLAUDE.md).
            secondSale.SaleId.Should().BeGreaterThan(0, "sanity: the second sale really did complete against the live database");
        }
        finally
        {
            Directory.Delete(usbDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FR11_12_APreviewReadsTheDataDateWithoutAPassphrase()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);
        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        var preview = await fixture.Resolve<GuidedRestoreService>().PreviewAsync(snapshot.FilePath);

        preview.TakenAt.Should().Be(snapshot.TakenAt);
        preview.SchemaVersion.Should().Be(snapshot.SchemaVersion);
    }

    /// <summary>
    /// The typed-confirmation gate: a wrong string is rejected before anything else runs at all -
    /// no safety backup, no staging file, no audit entry (SRS FR-11.12's "require explicit typed
    /// confirmation").
    /// </summary>
    [Fact]
    public async Task FR11_12_AWrongTypedConfirmationIsRejectedWithNothingChanged()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);
        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(1);

        var restore = async () => await fixture.Resolve<GuidedRestoreService>().RestoreAsync(
            new GuidedRestoreRequest(snapshot.FilePath, Passphrase, "yes please restore it"));

        (await restore.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain(GuidedRestoreConfirmation.RequiredPhrase);

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            1, "a rejected confirmation must not take a safety backup - nothing may run before the phrase is checked");

        File.Exists(PendingRestoreLocation.StagingFilePath(fixture.SnapshotDirectory)).Should().BeFalse(
            "nothing should be staged for the next start-up either");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = '" + BackupAuditActions.DatabaseRestored + "';"))
            .Should().Be(0, "a rejected restore must leave no audit trail claiming one happened");
    }

    /// <summary>Case-sensitivity is part of "typed exactly", not an accident of string comparison.</summary>
    [Fact]
    public async Task FR11_12_TheTypedConfirmationIsCaseSensitive()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);
        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        var restore = async () => await fixture.Resolve<GuidedRestoreService>().RestoreAsync(
            new GuidedRestoreRequest(snapshot.FilePath, Passphrase, "restore"));

        await restore.Should().ThrowAsync<ArgumentException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(1);
    }

    private static async Task<string?> ScalarFromRawFileAsync(string filePath, byte[] key, string sql)
    {
        SQLitePCL.Batteries_V2.Init();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA key = \"x'" + Convert.ToHexString(key) + "'\";";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<CompletedSale> CompleteOneAsync(SaleFixture fixture, decimal quantity = 1m)
    {
        var lines = new List<SaleLineRequest> { new(await SeededVariantIdAsync(fixture), quantity) };

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                await SeededUserIdAsync(fixture),
                await SeededShiftIdAsync(fixture),
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
