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
    /// <summary>
    /// Every migration in the chain, in order, as EF ids. The chain is what a till applies on
    /// start-up, so the count here is the count of migrations that must be reviewed together.
    /// </summary>
    private static readonly string[] Chain =
    [
        "20260904121117_Skeleton0001",
        "20260905005921_FullSchema0002",
        "20260905010014_ProductForeignKeys0003",
        "20260905010104_ProductSearch0004",
    ];

    [Fact]
    public async Task NFR_M3_TheChainAppliesToAnEmptyDatabaseAndLeavesItIntact()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        database.MigrationResult.AppliedMigrations.Should().Equal(Chain);

        (await database.ScalarAsync("PRAGMA integrity_check;")).Should().Be("ok");

        // __EFMigrationsHistory and __EFMigrationsLock are EF's own bookkeeping, not schema.
        // product_search's four shadow tables are SQLite's, created by the FTS5 module.
        var tables = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'table' " +
            "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\' " +
            "AND name NOT LIKE 'product\\_search\\_%' ESCAPE '\\' ORDER BY name;");

        tables.Should().HaveCount(41, "forty documented tables plus the product_search index");
        tables.Should().Contain("product_search");
    }

    /// <summary>
    /// A first run has nothing to lose, so it must not litter the backup folder. If it did, the
    /// only file there would be an empty database and the real one would be harder to spot.
    /// </summary>
    [Fact]
    public async Task NFR_M3_AFirstRunOnAnEmptyDatabaseTakesNoBackup()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

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
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

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
        first.AppliedMigrations.Should().Equal(Chain);

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await TradingDaySeed.ApplyAsync(connection);
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
    /// <para>
    /// This is also the migration test the engineering guide asks for on the seeded side.
    /// <c>FullSchema0002</c> gives <c>product</c> two foreign keys, and EF's SQLite provider adds
    /// a foreign key by copying the table into a new one - so a database with rows in it takes a
    /// different path through the migration from an empty one, and it is the one a real till
    /// takes.
    /// </para>
    /// <para>
    /// The pending state is genuine: the database is brought up to <c>Skeleton0001</c>, seeded,
    /// and then handed to the runner with two migrations outstanding.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NFR_M3_APopulatedDatabaseIsBackedUpBeforeMigrating()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await MigratedDatabase.MigrateToAsync(factory, "Skeleton0001");

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await TradingDaySeed.ApplySkeletonAsync(connection);

            // A day's trade, copied aside so the assertions below can show it came through the
            // rebuild of `product` unchanged rather than merely still existing.
            await using var archive = connection.CreateCommand();
            archive.CommandText =
                "CREATE TABLE archived_sale AS SELECT bill_no, total, status FROM sale;" +
                "CREATE TABLE archived_payment AS SELECT tender_type, amount FROM payment;";
            await archive.ExecuteNonQueryAsync();
        }

        var runner = new MigrationRunner(factory, fixture.DataDirectory);
        var result = await runner.ApplyPendingMigrationsAsync();

        result.AppliedMigrations.Should().Equal(Chain[1], Chain[2], Chain[3]);
        result.BackupFilePath.Should().NotBeNull();
        File.Exists(result.BackupFilePath!).Should().BeTrue();
        Path.GetFileName(result.BackupFilePath!).Should().StartWith("counterpoint-pre-");

        await using (var check = factory.OpenConfiguredConnection())
        {
            await using var command = check.CreateCommand();

            command.CommandText = "PRAGMA integrity_check;";
            (await command.ExecuteScalarAsync()).Should().Be("ok");

            command.CommandText = "PRAGMA foreign_key_check;";
            (await command.ExecuteScalarAsync()).Should().BeNull();

            // The rebuilt table kept every row and every value, not just its shape.
            command.CommandText = "SELECT code || '|' || name || '|' || cost_avg FROM product;";
            (await command.ExecuteScalarAsync()).Should().Be("P-001|Galvanised bolt M8|900000");

            command.CommandText = "SELECT count(*) FROM sale;";
            (await command.ExecuteScalarAsync()).Should().Be(1L);

            // The catalogue that was already there is searchable. ProductSearch0004's triggers only
            // see what happens after them, so without its backfill an upgraded till would come back
            // with a working search box that finds nothing - and nothing would fail, because an
            // empty index is a valid index.
            command.CommandText =
                "SELECT count(*) FROM product_search WHERE product_search MATCH 'galvanised';";
            (await command.ExecuteScalarAsync()).Should().Be(1L);
        }

        await AssertBackupIsUsableAsync(fixture, result.BackupFilePath!);
    }

    /// <summary>
    /// AC-15 for the upgrade path: a power cut part-way through <c>ProductForeignKeys0003</c> must
    /// leave a database the next start can finish migrating, not one it can never migrate again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That migration is the one step of this upgrade that cannot be a single transaction. SQLite
    /// ignores <c>PRAGMA foreign_keys = 0</c> inside a transaction, so EF emits it
    /// transaction-suppressed and the migration commits in three groups, with the
    /// <c>__EFMigrationsHistory</c> row written after the last. A cut inside the second group
    /// rolls that group back but leaves the first one's <c>ef_temp_product</c> durably on disk -
    /// and without the <c>DROP TABLE IF EXISTS</c> that opens the migration, the retry would die
    /// on "table ef_temp_product already exists" for ever.
    /// </para>
    /// <para>
    /// The interrupted state is manufactured rather than genuinely interrupted, because a test
    /// cannot cut the power: the database is taken to <c>FullSchema0002</c> and a populated
    /// <c>ef_temp_product</c> is left behind, which is exactly what the first command group
    /// commits.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AC_15_AnUpgradeInterruptedDuringTheProductRebuildFinishesOnTheNextStart()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await MigratedDatabase.MigrateToAsync(factory, "FullSchema0002");

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await TradingDaySeed.ApplyAsync(connection);

            await using var interrupted = connection.CreateCommand();
            interrupted.CommandText = "CREATE TABLE ef_temp_product AS SELECT * FROM product;";
            await interrupted.ExecuteNonQueryAsync();
        }

        var runner = new MigrationRunner(factory, fixture.DataDirectory);
        var result = await runner.ApplyPendingMigrationsAsync();

        result.AppliedMigrations.Should().Equal(Chain[2], Chain[3]);

        await using (var check = factory.OpenConfiguredConnection())
        {
            await using var command = check.CreateCommand();

            command.CommandText = "PRAGMA integrity_check;";
            (await command.ExecuteScalarAsync()).Should().Be("ok");

            command.CommandText = "PRAGMA foreign_key_check;";
            (await command.ExecuteScalarAsync()).Should().BeNull();

            // The rebuild really did happen: the foreign keys are there and the leftovers are not.
            command.CommandText =
                "SELECT count(*) FROM pragma_foreign_key_list('product') WHERE \"table\" IN ('brand','category');";
            (await command.ExecuteScalarAsync()).Should().Be(2L);

            command.CommandText =
                "SELECT count(*) FROM sqlite_schema WHERE name = 'ef_temp_product';";
            (await command.ExecuteScalarAsync()).Should().Be(0L);

            // The day's trade came through it.
            command.CommandText = "SELECT code FROM product WHERE id = 1;";
            (await command.ExecuteScalarAsync()).Should().Be("P-001");
        }
    }

    /// <summary>
    /// A migration that fails must say where the file as it stood is sitting. A bare
    /// <c>SqliteException</c> tells the operator nothing about the backup the runner just took.
    /// </summary>
    [Fact]
    public async Task NFR_M3_AFailedMigrationNamesThePreMigrationBackup()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await MigratedDatabase.MigrateToAsync(factory, "Skeleton0001");

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await TradingDaySeed.ApplySkeletonAsync(connection);

            // Stands where FullSchema0002 is about to create its own, so the migration aborts.
            await using var blocker = connection.CreateCommand();
            blocker.CommandText = "CREATE TABLE app_setting (whatever TEXT);";
            await blocker.ExecuteNonQueryAsync();
        }

        var runner = new MigrationRunner(factory, fixture.DataDirectory);

        var act = async () => await runner.ApplyPendingMigrationsAsync();

        var thrown = await act.Should().ThrowAsync<SchemaMigrationException>();
        thrown.Which.Message.Should().Contain("counterpoint-pre-").And.Contain("must not trade");
        thrown.Which.InnerException.Should().NotBeNull();
    }

    /// <summary>
    /// A row that the constraint about to be added would forbid stops the upgrade rather than
    /// being copied through it. SQLite refuses the rebuild's COMMIT; the runner turns that into a
    /// message that names the backup.
    /// </summary>
    [Fact]
    public async Task DM_04_ARowTheNewConstraintForbidsStopsTheUpgrade()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await MigratedDatabase.MigrateToAsync(factory, "FullSchema0002");

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await TradingDaySeed.ApplyAsync(connection);

            // A category that does not exist. Legal now - category_id is still a plain column at
            // FullSchema0002 - and an orphan the moment ProductForeignKeys0003 lands.
            await using var orphan = connection.CreateCommand();
            orphan.CommandText = "UPDATE product SET category_id = 4242 WHERE id = 1;";
            await orphan.ExecuteNonQueryAsync();
        }

        var runner = new MigrationRunner(factory, fixture.DataDirectory);

        var act = async () => await runner.ApplyPendingMigrationsAsync();

        var thrown = await act.Should().ThrowAsync<SchemaMigrationException>();
        thrown.Which.Message.Should().Contain("counterpoint-pre-").And.Contain("must not trade");

        // And it stopped rather than half-applied: the till is still at FullSchema0002.
        await using var check = factory.OpenConfiguredConnection();
        await using var command = check.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM pragma_foreign_key_list('product') WHERE \"table\" = 'category';";
        (await command.ExecuteScalarAsync()).Should().Be(0L);
    }

    /// <summary>
    /// DM-04: <c>PRAGMA integrity_check</c> says nothing about foreign keys, so the runner checks
    /// both. This is the case <c>integrity_check</c> alone would wave through - an orphan in a
    /// table the migration never touches, which SQLite has no reason to notice on its own.
    /// </summary>
    /// <remarks>
    /// The orphan is created with <c>PRAGMA foreign_keys = OFF</c> on the test's own connection,
    /// because with the pragma on - as <c>PosConnectionFactory</c> sets it - the insert could not
    /// happen. That is exactly how such a row gets into a real file: a repair session with
    /// <c>sqlite3</c>, which does not set the pragma either.
    /// </remarks>
    [Fact]
    public async Task DM_04_AnOrphanElsewhereInTheFileStopsTheUpgrade()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await MigratedDatabase.MigrateToAsync(factory, "FullSchema0002");

        await using (var connection = factory.OpenConfiguredConnection())
        {
            await TradingDaySeed.ApplyAsync(connection);

            await using var orphan = connection.CreateCommand();
            orphan.CommandText =
                "PRAGMA foreign_keys = OFF;" +
                "INSERT INTO barcode (id, product_variant_id, barcode, is_primary)" +
                " VALUES (99, 4242, '0000000000000', 0);" +
                "PRAGMA foreign_keys = ON;";
            await orphan.ExecuteNonQueryAsync();
        }

        var runner = new MigrationRunner(factory, fixture.DataDirectory);

        var act = async () => await runner.ApplyPendingMigrationsAsync();

        var thrown = await act.Should().ThrowAsync<SchemaMigrationException>();
        thrown.Which.Message.Should().Contain("break a foreign key")
            .And.Contain("barcode -> product_variant")
            .And.Contain("counterpoint-pre-");
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

            // The seeded trade: a backup that loses the day's bills is not a backup.
            await using var bill = opened.CreateCommand();
            bill.CommandText = "SELECT bill_no || ' ' || total || ' ' || status FROM archived_sale;";
            (await bill.ExecuteScalarAsync()).Should().Be("INV-2026-000001 2875000 COMPLETED");

            await using var tender = opened.CreateCommand();
            tender.CommandText = "SELECT tender_type || ' ' || amount FROM archived_payment;";
            (await tender.ExecuteScalarAsync()).Should().Be("CASH 2875000");

            // Taken *before* the chain ran, which nothing else here would catch: move the backup
            // call after MigrateAsync and the copy would carry FullSchema0002's own tables.
            await using var beforeMigrating = opened.CreateCommand();
            beforeMigrating.CommandText =
                "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = 'category';";
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
