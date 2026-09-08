using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Backup;
using Counterpoint.Backup.Restore;
using Counterpoint.Backup.Snapshots;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// P0-T07: the snapshot/encrypt/restore pipeline, over a real, migrated, SQLCipher-encrypted
/// database and a real backup file on disk - never the in-memory provider (SRS FR-11.1-11.4).
/// </summary>
public sealed class BackupRoundTripTests
{
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    private const string Passphrase = "correct horse battery staple";

    /// <summary>
    /// Done-when items 1 and 2: a backup file is produced, encrypted, checksummed and recorded,
    /// and restoring it into a scratch location reproduces the sale exactly.
    /// </summary>
    [Fact]
    public async Task FR11_1_And_FR11_4_RestoringASnapshotReproducesTheSaleExactly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var completed = await CompleteOneAsync(fixture, quantity: 2m);

        var originalSale = await fixture.ScalarAsync(
            "SELECT bill_no || '|' || total || '|' || status FROM sale WHERE id = "
            + completed.SaleId + ";");
        var originalLine = await fixture.ScalarAsync(
            "SELECT description || '|' || unit_price || '|' || unit_cost || '|' || qty_base "
            + "|| '|' || line_total FROM sale_line WHERE sale_id = " + completed.SaleId + ";");

        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        snapshot.FilePath.Should().StartWith(fixture.SnapshotDirectory);
        File.Exists(snapshot.FilePath).Should().BeTrue("the encrypted backup file must be on disk");

        var destination = Path.Combine(fixture.SnapshotDirectory, "restored", "restored.db");

        var restored = await fixture.Resolve<RestoreService>().RestoreAsync(
            snapshot.FilePath, Passphrase, destination);

        restored.TotalRowCount.Should().BeGreaterThan(0, "the restored database has real rows in it");
        restored.SchemaVersion.Should().Be(snapshot.SchemaVersion);

        // Open the restored, still SQLCipher-encrypted, file directly with the till's own key -
        // proving the bytes on disk are a real database and not merely a file that decrypted
        // without error (RestoreService already proved that with PRAGMA integrity_check; this
        // proves the actual sale is in it, unchanged).
        var key = fixture.Resolve<IDatabaseKeyStore>().GetOrCreateKey();

        var restoredSale = await ScalarFromRawFileAsync(
            destination, key,
            "SELECT bill_no || '|' || total || '|' || status FROM sale WHERE id = "
            + completed.SaleId + ";");
        var restoredLine = await ScalarFromRawFileAsync(
            destination, key,
            "SELECT description || '|' || unit_price || '|' || unit_cost || '|' || qty_base "
            + "|| '|' || line_total FROM sale_line WHERE sale_id = " + completed.SaleId + ";");

        restoredSale.Should().Be(originalSale, "a restore must reproduce the sale exactly, byte for byte");
        restoredLine.Should().Be(originalLine, "sale_line's snapshot of price and cost must survive a round trip too");
    }

    /// <summary>Done-when item 3: a wrong passphrase fails cleanly, not with a raw crypto exception.</summary>
    [Fact]
    public async Task FR11_4_WrongPassphraseFailsCleanly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);
        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        var destination = Path.Combine(fixture.SnapshotDirectory, "restored", "wrong-passphrase.db");

        var restore = async () => await fixture.Resolve<RestoreService>().RestoreAsync(
            snapshot.FilePath, "not-the-right-passphrase", destination);

        (await restore.Should().ThrowAsync<BackupRestoreException>())
            .Which.Message.Should().Contain(
                "passphrase is incorrect",
                "a wrong passphrase must fail with a plain-language message, never a raw crypto exception (SRS FR-11.4)");

        File.Exists(destination).Should().BeFalse(
            "nothing should be written to the destination until the backup has actually been decrypted");
    }

    /// <summary>
    /// A tampered ciphertext is refused at the checksum stage, before the expensive Argon2id
    /// derivation is even attempted - it is impossible to observe the derivation directly from a
    /// test, so this asserts the one thing that is observable: the message is checksum-specific,
    /// not the passphrase message FR11_4_WrongPassphraseFailsCleanly proves.
    /// </summary>
    [Fact]
    public async Task ATamperedBackupFileFailsAtTheChecksumBeforeDecryption()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);
        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        var bytes = await File.ReadAllBytesAsync(snapshot.FilePath);

        // Flip one bit well past the header/tag/checksum block, inside the ciphertext itself, so
        // this is a genuinely tampered file rather than a header that fails to parse at all.
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(snapshot.FilePath, bytes);

        var destination = Path.Combine(fixture.SnapshotDirectory, "restored", "tampered.db");

        var restore = async () => await fixture.Resolve<RestoreService>().RestoreAsync(
            snapshot.FilePath, Passphrase, destination);

        (await restore.Should().ThrowAsync<BackupRestoreException>())
            .Which.Message.Should().Contain("damaged")
            .And.NotContain("passphrase is incorrect", "a checksum failure is a different fault than a wrong passphrase");

        File.Exists(destination).Should().BeFalse();
    }

    /// <summary>
    /// A file truncated inside the header block - before <c>BackupFileHeader.ReadAsync</c>
    /// can even finish parsing it - is refused with its own message, distinct from both the
    /// checksum and passphrase failures above.
    /// </summary>
    /// <remarks>
    /// A file truncated <em>after</em> the header (but before all its ciphertext) is caught
    /// instead by the checksum mismatch in <see cref="ATamperedBackupFileFailsAtTheChecksumBeforeDecryption"/>'s
    /// sibling case: <c>RestoreService.RestoreAsync</c> sizes its ciphertext buffer from however
    /// many bytes are actually left in the stream, not from a length recorded in the header, so a
    /// shorter file there simply becomes ciphertext whose checksum no longer matches - still
    /// refused, just via the checksum message rather than the "truncated" one. This test proves
    /// the header-level truncation path specifically.
    /// </remarks>
    [Fact]
    public async Task ATruncatedBackupFileFailsCleanly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);
        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        var bytes = await File.ReadAllBytesAsync(snapshot.FilePath);

        // 10 bytes: past the 4-byte magic and 1-byte format version, but well inside the 16-byte
        // salt that follows - short enough that BackupFileHeader.ReadAsync hits end of stream
        // while still reading the header itself, before there is a checksum to compare at all.
        await File.WriteAllBytesAsync(snapshot.FilePath, bytes[..10]);

        var destination = Path.Combine(fixture.SnapshotDirectory, "restored", "truncated.db");

        var restore = async () => await fixture.Resolve<RestoreService>().RestoreAsync(
            snapshot.FilePath, Passphrase, destination);

        (await restore.Should().ThrowAsync<BackupRestoreException>())
            .Which.Message.Should().Contain("truncated");

        File.Exists(destination).Should().BeFalse();
    }

    /// <summary>
    /// The <c>backup_record</c> row a snapshot writes must carry values that are actually legal
    /// under the CHECK constraints <c>SchemaConformanceTests</c> already exercises for
    /// <c>usb_status</c> and <c>cloud_status</c> - Phase 0 has no USB copy and no cloud uploader
    /// yet, so this is 'NA' and 'SKIPPED' today, not merely "some string that happens to work".
    /// </summary>
    [Fact]
    public async Task ASnapshotRecordsABackupRecordRowThatSatisfiesItsOwnCheckConstraints()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(0);

        var snapshot = await fixture.Resolve<SnapshotService>().CreateSnapshotAsync();

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(1);

        var row = await fixture.ScalarAsync(
            "SELECT filename || '|' || size_bytes || '|' || checksum || '|' || schema_ver || '|' "
            + "|| usb_status || '|' || cloud_status FROM backup_record ORDER BY id DESC LIMIT 1;");

        row.Should().Be(
            snapshot.Filename + "|" + snapshot.SizeBytes + "|" + snapshot.Checksum + "|"
            + snapshot.SchemaVersion + "|NA|SKIPPED",
            "NA: no USB copy exists until P1-T15; SKIPPED: no cloud uploader exists until Phase 4 "
            + "(SRS FR-11.3, FR-11.5, CLAUDE.md 'Not cloud-dependent')");
    }

    /// <summary>
    /// The class remarks on <c>SnapshotService</c> claim a specific crash-safety ordering: the
    /// encrypted file is renamed into place <em>before</em> <c>backup_record</c> is touched, so a
    /// failure recording the row leaves a discoverable, restorable file behind rather than
    /// silently losing it. This proves the code actually behaves that way, with a fake record
    /// store standing in for the crash.
    /// </summary>
    [Fact]
    public async Task WhenRecordingTheBackupRecordRowFailsTheBackupFileSurvivesIntactOnDisk()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await CompleteOneAsync(fixture);

        var throwingService = new SnapshotService(
            fixture.Resolve<IDatabaseSnapshotSource>(),
            fixture.Resolve<IBackupPassphraseStore>(),
            new ThrowingBackupRecordStore(),
            fixture.Resolve<SnapshotOptions>(),
            fixture.Resolve<Argon2Parameters>(),
            fixture.Resolve<TimeProvider>());

        var act = async () => await throwingService.CreateSnapshotAsync();

        await act.Should().ThrowAsync<InvalidOperationException>("the fake store simulates a crash recording the row");

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            0, "the row was never written, exactly as the fake store demands");

        Directory.GetFiles(fixture.SnapshotDirectory, "*.writing").Should().BeEmpty(
            "no half-written temporary file should be left behind either");

        var finished = Directory.GetFiles(fixture.SnapshotDirectory, "counterpoint-*.cpbk");
        finished.Should().ContainSingle(
            "the encrypted file was renamed into place before the record store was ever called, "
            + "so a crash there must not delete or corrupt it - a later re-scan of the folder can "
            + "still find it");

        // And it is not merely present - it has to still be a real, restorable backup.
        var destination = Path.Combine(fixture.SnapshotDirectory, "restored", "orphaned.db");
        var restored = await fixture.Resolve<RestoreService>().RestoreAsync(finished[0], Passphrase, destination);

        restored.TotalRowCount.Should().BeGreaterThan(0);
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

    /// <summary>Stands in for a crash writing <c>backup_record</c>, to prove the ordering invariant.</summary>
    private sealed class ThrowingBackupRecordStore : IBackupRecordStore
    {
        public Task RecordAsync(NewBackupRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated crash: the record was never written.");
    }
}
