using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Brings the local database up to the schema this build expects: pre-migration backup, migrate,
/// record the version, check the append-only triggers survived, verify integrity (NFR-M3, DM-05).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not driven through <see cref="IUnitOfWork"/> or
/// <see cref="IPosDbContextFactory"/>. Migrating is not a business operation, and two of its
/// steps cannot run inside a transaction at all: EF's migration command executor commits and
/// discards the ambient transaction when it meets a transaction-suppressed command (the SQLite
/// generator emits <c>PRAGMA foreign_keys</c> that way around a table rebuild), and
/// <c>VACUUM INTO</c> fails outright with "cannot VACUUM from within a transaction". Nesting this
/// inside <c>ExecuteInTransactionAsync</c> would let EF commit the unit of work's
/// <c>BEGIN IMMEDIATE</c> out from under it.
/// </para>
/// <para>
/// It still takes the single write lease, so nothing else writes while the schema changes.
/// </para>
/// </remarks>
public sealed class MigrationRunner
{
    private readonly IPosConnectionFactory _connectionFactory;
    private readonly PosDataDirectory _dataDirectory;

    public MigrationRunner(IPosConnectionFactory connectionFactory, PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(dataDirectory);

        _connectionFactory = connectionFactory;
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Applies every pending migration. Safe to call on every start: with nothing pending it does
    /// no writes, takes no backup and returns an empty result.
    /// </summary>
    /// <exception cref="SchemaMigrationException">
    /// A trigger did not survive the chain, or <c>PRAGMA integrity_check</c> did not return
    /// <c>ok</c>. The message names the pre-migration backup when there is one.
    /// </exception>
    public async Task<MigrationRunResult> ApplyPendingMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // No transaction is open at this point, and none may be: see the class remarks.
        var lease = await _connectionFactory.AcquireWriteConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            var options = new DbContextOptionsBuilder<PosDbContext>()

                // contextOwnsConnection: false - the connection belongs to the lease and outlives
                // this context.
                .UseSqlite(lease.Connection, contextOwnsConnection: false)
                .Options;

            using var context = new PosDbContext(options);

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false)).ToList();

            if (pending.Count == 0)
            {
                return new MigrationRunResult([], null, stopwatch.Elapsed);
            }

            var backupFilePath = await TryTakePreMigrationBackupAsync(lease.Connection, cancellationToken)
                .ConfigureAwait(false);

            // No outer transaction wrapping the chain, on purpose. EF opens a transaction per
            // migration and commits it together with that migration's __EFMigrationsHistory row,
            // so each migration is individually atomic - SQLite DDL is transactional. What an
            // outer wrap across the whole chain would buy is bought instead by the backup above.
            // Do not "fix" this by wrapping it: see the class remarks for why it cannot work.
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            // Written in its own transaction, after the chain. A crash between the two leaves
            // __EFMigrationsHistory ahead of schema_version; the reconciliation below repairs
            // that on the next start rather than assuming it cannot happen.
            await RecordSchemaVersionsAsync(context, cancellationToken).ConfigureAwait(false);

            await VerifyTriggersSurvivedAsync(lease.Connection, backupFilePath, cancellationToken)
                .ConfigureAwait(false);
            await VerifyIntegrityAsync(lease.Connection, backupFilePath, cancellationToken)
                .ConfigureAwait(false);

            return new MigrationRunResult(pending, backupFilePath, stopwatch.Elapsed);
        }
    }

    private static async Task<object?> ScalarAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a <c>schema_version</c> row for every applied migration that has none.
    /// </summary>
    /// <remarks>
    /// <c>__EFMigrationsHistory</c> is EF's mechanism; <c>schema_version</c> is the documented
    /// authority (docs/01_DATA_MODEL.md §8) and what a backup header records. Reconciling rather
    /// than blindly inserting the ids from this run keeps the two converging after an interrupted
    /// upgrade.
    /// </remarks>
    private static async Task RecordSchemaVersionsAsync(
        PosDbContext context,
        CancellationToken cancellationToken)
    {
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        var recorded = await context.SchemaVersions
            .Select(schemaVersion => schemaVersion.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missing = applied.Except(recorded, StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var appliedAt = DateTimeOffset.Now;
        foreach (var version in missing)
        {
            context.SchemaVersions.Add(new SchemaVersion { Version = version, AppliedAt = appliedAt });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyTriggersSurvivedAsync(
        DbConnection connection,
        string? backupFilePath,
        CancellationToken cancellationToken)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'trigger';";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                present.Add(reader.GetString(0));
            }
        }

        var missing = AppendOnlyTables.AllTriggerNames
            .Where(name => !present.Contains(name))
            .ToList();

        if (missing.Count > 0)
        {
            throw new SchemaMigrationException(
                "The append-only protection is missing from the upgraded database: no trigger " +
                string.Join(", ", missing) + ". EF Core's SQLite provider rebuilds a table for " +
                "almost any alter and a rebuild drops that table's triggers, so the migration " +
                "that altered the table must re-create them. " + BackupHint(backupFilePath));
        }
    }

    private static async Task VerifyIntegrityAsync(
        DbConnection connection,
        string? backupFilePath,
        CancellationToken cancellationToken)
    {
        var result = (await ScalarAsync(connection, "PRAGMA integrity_check;", cancellationToken)
            .ConfigureAwait(false))?.ToString();

        if (!string.Equals(result, "ok", StringComparison.Ordinal))
        {
            throw new SchemaMigrationException(
                "PRAGMA integrity_check reported '" + result + "' after migrating, so the " +
                "database file is damaged. Do not trade against it. " + BackupHint(backupFilePath));
        }
    }

    private static string BackupHint(string? backupFilePath) =>
        backupFilePath is null
            ? "There is no pre-migration backup because the database was empty before this run."
            : "The database as it was before this run is at " + backupFilePath + ".";

    /// <summary>
    /// Copies the encrypted database file before the first migration write (NFR-M3), and returns
    /// the path - or null when the database had nothing in it to lose.
    /// </summary>
    /// <remarks>
    /// <c>VACUUM INTO</c>, not a file copy: it is consistent without stopping writers, and on
    /// SQLCipher the output is encrypted with the same key. This is *not* the FR-11 backup
    /// snapshot of P0-T07 - no compression, no separate passphrase, no <c>backup_record</c> row.
    /// The two are deliberately different things. Retention is P4's problem; nothing is pruned
    /// here.
    /// </remarks>
    private async Task<string?> TryTakePreMigrationBackupAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var userTables = Convert.ToInt64(
            await ScalarAsync(
                connection,
                "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%';",
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        if (userTables == 0)
        {
            return null;
        }

        Directory.CreateDirectory(_dataDirectory.PreMigrationBackupDirectory);

        var fileName = "counterpoint-pre-" +
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db";
        var target = Path.Combine(_dataDirectory.PreMigrationBackupDirectory, fileName);

        // The target is interpolated into SQL because VACUUM INTO takes a literal, not a
        // parameter. Doubling the quote is the whole of SQLite's string escaping.
        await ExecuteAsync(
            connection,
            "VACUUM INTO '" + target.Replace("'", "''", StringComparison.Ordinal) + "';",
            cancellationToken).ConfigureAwait(false);

        return target;
    }
}

/// <summary>What one call to <see cref="MigrationRunner.ApplyPendingMigrationsAsync"/> did.</summary>
/// <param name="AppliedMigrations">
/// Migration ids applied by this run, in order. Empty means the database was already current.
/// </param>
/// <param name="BackupFilePath">
/// The pre-migration copy, or null when nothing was applied or the database was empty.
/// </param>
/// <param name="Duration">Wall-clock time of the whole run.</param>
public sealed record MigrationRunResult(
    IReadOnlyList<string> AppliedMigrations,
    string? BackupFilePath,
    TimeSpan Duration);
