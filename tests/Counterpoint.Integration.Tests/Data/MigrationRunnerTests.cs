using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// Automatic schema migration with a pre-migration backup and an integrity check (NFR-M3, DM-05).
/// </summary>
public sealed class MigrationRunnerTests
{
    /// <summary>Every table docs/01_DATA_MODEL.md's skeleton subset (§13) is meant to create.</summary>
    private static readonly string[] SkeletonTables =
    [
        "app_user", "audit_log", "number_sequence", "payment", "print_job", "product",
        "product_variant", "sale", "sale_line", "schema_version", "shift", "stock_balance",
        "stock_movement", "tax_class", "uom",
    ];

    [Fact]
    public async Task NFR_M3_TheChainAppliesToAnEmptyDatabaseAndLeavesItIntact()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        database.MigrationResult.AppliedMigrations.Should().ContainSingle()
            .Which.Should().EndWith("_Skeleton0001");

        (await database.ScalarAsync("PRAGMA integrity_check;")).Should().Be("ok");

        // __EFMigrationsHistory and __EFMigrationsLock are EF's own bookkeeping, not schema.
        var tables = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'table' " +
            "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\' ORDER BY name;");

        tables.Should().BeEquivalentTo(SkeletonTables);
    }

    /// <summary>
    /// A first run has nothing to lose, so it must not litter the backup folder. If it did, the
    /// only file there would be an empty database and the real one would be harder to spot.
    /// </summary>
    [Fact]
    public async Task NFR_M3_AFirstRunOnAnEmptyDatabaseTakesNoBackup()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        database.MigrationResult.BackupFilePath.Should().BeNull();

        Directory.GetFiles(database.DataDirectory.PreMigrationBackupDirectory)
            .Should().BeEmpty();
    }

    /// <summary>
    /// <c>schema_version</c> is the documented authority (docs/01_DATA_MODEL.md §8);
    /// <c>__EFMigrationsHistory</c> is EF's own mechanism. They must agree.
    /// </summary>
    [Fact]
    public async Task DM_05_SchemaVersionAgreesWithTheMigrationHistory()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var recorded = await database.ColumnAsync("SELECT version FROM schema_version ORDER BY version;");
        var history = await database.ColumnAsync(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";");

        recorded.Should().BeEquivalentTo(history);
        recorded.Should().BeEquivalentTo(database.MigrationResult.AppliedMigrations);

        (await database.ScalarAsync("SELECT applied_at FROM schema_version LIMIT 1;"))
            .Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2}$");
    }

    /// <summary>
    /// Runs on every start-up, so the common case is "nothing to do". It must then write nothing,
    /// back up nothing and leave a seeded database exactly as it found it.
    /// </summary>
    [Fact]
    public async Task NFR_M3_ARerunWithNothingPendingIsANoOp()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        var runner = new MigrationRunner(factory, fixture.DataDirectory);
        var first = await runner.ApplyPendingMigrationsAsync();
        first.AppliedMigrations.Should().ContainSingle();

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await SkeletonSeed.ApplyAsync(connection);
        }

        var second = await runner.ApplyPendingMigrationsAsync();

        second.AppliedMigrations.Should().BeEmpty();
        second.BackupFilePath.Should().BeNull();
        Directory.GetFiles(fixture.DataDirectory.PreMigrationBackupDirectory).Should().BeEmpty();

        await using var check = factory.OpenConfiguredConnection();
        await using var command = check.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sale;";
        (await command.ExecuteScalarAsync()).Should().Be(1L);

        command.CommandText = "PRAGMA integrity_check;";
        (await command.ExecuteScalarAsync()).Should().Be("ok");
    }

    /// <summary>
    /// NFR-M3's other half: a database with something in it gets a copy taken before the first
    /// migration write, and that copy must be openable, complete and still encrypted.
    /// </summary>
    /// <remarks>
    /// The "populated database with a migration pending" state is manufactured by deleting the
    /// <c>__EFMigrationsHistory</c> rows from an already-migrated file. That is exactly the shape
    /// of the real case - user tables present, a migration outstanding - without needing a second
    /// migration that P1-T01 has not written yet.
    /// </remarks>
    [Fact]
    public async Task NFR_M3_APopulatedDatabaseIsBackedUpBeforeMigrating()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        var runner = new MigrationRunner(factory, fixture.DataDirectory);
        await runner.ApplyPendingMigrationsAsync();

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await SkeletonSeed.ApplyAsync(connection);

            // A day's trade, carried across the drops below so the backup has real seeded rows in
            // it and not only the marker table. `sale` itself cannot survive - the migration is
            // about to create it - but its contents can.
            await using (var archive = connection.CreateCommand())
            {
                archive.CommandText =
                    "CREATE TABLE archived_sale AS SELECT bill_no, total, status FROM sale;" +
                    "CREATE TABLE archived_payment AS SELECT tender_type, amount FROM payment;";
                await archive.ExecuteNonQueryAsync();
            }

            await using var forget = connection.CreateCommand();

            // Pretend Skeleton0001 was never applied, so the next run has work to do against a
            // database that already holds a day's trade.
            forget.CommandText = "DELETE FROM \"__EFMigrationsHistory\"; DROP TABLE sale_line;" +
                " DROP TABLE payment; DROP TABLE stock_movement; DROP TABLE audit_log;" +
                " DROP TABLE print_job; DROP TABLE stock_balance; DROP TABLE sale;" +
                " DROP TABLE shift; DROP TABLE product_variant; DROP TABLE product;" +
                " DROP TABLE number_sequence; DROP TABLE schema_version; DROP TABLE app_user;" +
                " DROP TABLE tax_class; DROP TABLE uom;";
            await forget.ExecuteNonQueryAsync();

            // Something worth losing that the migration will not touch.
            await using var evidence = connection.CreateCommand();
            evidence.CommandText = "CREATE TABLE takings (note TEXT); INSERT INTO takings VALUES ('day one');";
            await evidence.ExecuteNonQueryAsync();
        }

        var result = await runner.ApplyPendingMigrationsAsync();

        result.AppliedMigrations.Should().ContainSingle();
        result.BackupFilePath.Should().NotBeNull();
        File.Exists(result.BackupFilePath!).Should().BeTrue();
        Path.GetFileName(result.BackupFilePath!).Should().StartWith("counterpoint-pre-");

        await AssertBackupIsUsableAsync(fixture, result.BackupFilePath!);
    }

    /// <summary>
    /// The copy is SQLCipher output, holds the trade that was in the file, and is the file as it
    /// stood <em>before</em> the migration ran.
    /// </summary>
    private static async Task AssertBackupIsUsableAsync(TemporaryDataDirectory fixture, string backupPath)
    {
        var key = Convert.ToHexString(fixture.KeyStore.GetOrCreateKey());

        await using (var opened = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString()))
        {
            await opened.OpenAsync();

            await using var keyCommand = opened.CreateCommand();
            keyCommand.CommandText = "PRAGMA key = \"x'" + key + "'\";";
            await keyCommand.ExecuteNonQueryAsync();

            await using var integrity = opened.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            (await integrity.ExecuteScalarAsync()).Should().Be("ok");

            await using var takings = opened.CreateCommand();
            takings.CommandText = "SELECT note FROM takings;";
            (await takings.ExecuteScalarAsync()).Should().Be("day one");

            // The seeded trade, not just the marker table: a backup that loses the day's bills is
            // not a backup.
            await using var bill = opened.CreateCommand();
            bill.CommandText = "SELECT bill_no || ' ' || total || ' ' || status FROM archived_sale;";
            (await bill.ExecuteScalarAsync()).Should().Be("INV-2026-000001 2875000 COMPLETED");

            await using var tender = opened.CreateCommand();
            tender.CommandText = "SELECT tender_type || ' ' || amount FROM archived_payment;";
            (await tender.ExecuteScalarAsync()).Should().Be("CASH 2875000");

            // Taken *before* the chain ran, which nothing else here would catch: move the backup
            // call after MigrateAsync and the copy would carry the migration's own tables.
            await using var beforeMigrating = opened.CreateCommand();
            beforeMigrating.CommandText =
                "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = 'sale';";
            (await beforeMigrating.ExecuteScalarAsync()).Should().Be(0L);
        }

        // And it is opaque without the key: a stock SQLite build sees random bytes.
        if (SqliteCli.ExecutablePath is not null)
        {
            var probe = SqliteCli.Run(backupPath, ".tables");
            probe.AllOutput.Should().Contain("not a database");
        }
    }

    /// <summary>
    /// The runner takes the write lease itself. If it were driven through the unit of work, EF's
    /// migration executor would commit that BEGIN IMMEDIATE out from under the caller, and
    /// VACUUM INTO would fail outright inside a transaction.
    /// </summary>
    [Fact]
    public void TheRunnerDoesNotDependOnTheUnitOfWork()
    {
        var parameters = typeof(MigrationRunner).GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);

        parameters.Should().BeEquivalentTo(
            [typeof(IPosConnectionFactory), typeof(PosDataDirectory)]);
    }
}
