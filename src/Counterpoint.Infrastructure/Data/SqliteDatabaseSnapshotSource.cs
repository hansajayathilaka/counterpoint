using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Security;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The one place <c>Counterpoint.Backup</c> reaches a SQLCipher connection through, since it may
/// not reference this assembly directly (CLAUDE.md "Project boundaries", <c>P0-T07</c>). See
/// <see cref="IDatabaseSnapshotSource"/> for why the port exists at all.
/// </summary>
internal sealed class SqliteDatabaseSnapshotSource : IDatabaseSnapshotSource
{
    /// <summary>
    /// SQLitePCLRaw's native provider must be selected once per process, before any connection on
    /// it is opened. <see cref="PosConnectionFactory"/> does the same for the till's own
    /// connections; <see cref="VerifyRawCopyAsync"/> opens a second, independent file, so it needs
    /// its own idempotent init rather than relying on that one having already run.
    /// </summary>
    private static readonly Lazy<bool> NativeProvider = new(
        () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IPosConnectionFactory _connectionFactory;
    private readonly IDatabaseKeyStore _keyStore;

    public SqliteDatabaseSnapshotSource(IPosConnectionFactory connectionFactory, IDatabaseKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(keyStore);

        _connectionFactory = connectionFactory;
        _keyStore = keyStore;
    }

    /// <inheritdoc />
    public async Task<DatabaseSnapshotResult> CreateRawCopyAsync(
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        // VACUUM INTO refuses to overwrite an existing file. The caller always names a fresh
        // temporary path, but a leftover from a crashed previous attempt must not wedge every
        // run after it.
        if (File.Exists(destinationFilePath))
        {
            File.Delete(destinationFilePath);
        }

        var directory = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // VACUUM INTO only reads a consistent snapshot of the source database (safe under the
        // mandatory WAL journal mode, CLAUDE.md invariant 9) and writes a new file - it does not
        // modify the source, so it must not go through the single write-gated connection every
        // sale-completing transaction shares. Doing so would let a backup snapshot stall a sale,
        // violating CLAUDE.md invariant 7 ("Never block the sale"). A plain, ungated read
        // connection is enough, the same as SqliteProductLookup and friends use.
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Interpolated because VACUUM INTO takes a literal, not a parameter - the same
            // approach as MigrationRunner's pre-migration copy. Doubling the quote is the whole
            // of SQLite's string escaping.
            await ExecuteAsync(
                connection,
                "VACUUM INTO '" + destinationFilePath.Replace("'", "''", StringComparison.Ordinal) + "';",
                cancellationToken).ConfigureAwait(false);

            var schemaVersion = await ScalarAsync(
                connection,
                "SELECT version FROM schema_version ORDER BY applied_at DESC LIMIT 1;",
                cancellationToken).ConfigureAwait(false);

            var sizeBytes = new FileInfo(destinationFilePath).Length;

            return new DatabaseSnapshotResult(schemaVersion as string ?? "unknown", sizeBytes);
        }
    }

    /// <inheritdoc />
    public async Task<DatabaseVerificationResult> VerifyRawCopyAsync(
        string sqliteFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqliteFilePath);

        _ = NativeProvider.Value;

        var keyHex = Convert.ToHexString(RequireKey());

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteFilePath,
            Mode = SqliteOpenMode.ReadOnly,

            // Pooling off for the same reason PosConnectionFactory turns it off: a recycled
            // handle would skip the PRAGMA key below, and there is no way to prove from the
            // outside that it did not.
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA key = \"x'" + keyHex + "'\";", cancellationToken)
            .ConfigureAwait(false);

        string integrityMessage;
        try
        {
            // SQLCipher only validates the key against page 1 on the first real read, not on
            // PRAGMA key itself - so a wrong key surfaces here, as a plain SqliteException,
            // rather than at the statement above.
            var integrityResult = await ScalarAsync(connection, "PRAGMA integrity_check;", cancellationToken)
                .ConfigureAwait(false);
            integrityMessage = integrityResult?.ToString() ?? string.Empty;
        }
        catch (SqliteException ex)
        {
            return new DatabaseVerificationResult(
                false,
                "The database could not be read: " + ex.Message,
                new Dictionary<string, long>(StringComparer.Ordinal));
        }

        var rowCounts = await CountRowsPerTableAsync(connection, cancellationToken).ConfigureAwait(false);

        return new DatabaseVerificationResult(
            string.Equals(integrityMessage, "ok", StringComparison.Ordinal),
            integrityMessage,
            rowCounts);
    }

    private byte[] RequireKey()
    {
        var key = _keyStore.GetOrCreateKey();
        if (key.Length != DatabaseKey.SizeInBytes)
        {
            throw new InvalidOperationException(
                $"The database key store returned {key.Length} bytes; {DatabaseKey.SizeInBytes} are required.");
        }

        return key;
    }

    private static async Task<Dictionary<string, long>> CountRowsPerTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            // Table names come from sqlite_schema, never from a caller, so this is not
            // string-building an untrusted value into SQL.
            var count = await ScalarAsync(
                connection,
                "SELECT count(*) FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\";",
                cancellationToken).ConfigureAwait(false);
            counts[table] = Convert.ToInt64(count, CultureInfo.InvariantCulture);
        }

        return counts;
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

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
