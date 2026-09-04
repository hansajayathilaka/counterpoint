using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Security;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Opens SQLCipher-encrypted connections to the till's database: one long-lived write
/// connection behind a semaphore, and a fresh read connection per call
/// (engineering guide §4.8).
/// </summary>
public sealed class PosConnectionFactory : IPosConnectionFactory, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Applied to every connection, in this order, immediately after the key. Engineering guide
    /// §4.8 and CLAUDE.md invariant 9.
    /// </summary>
    private static readonly string[] ConnectionPragmas =
    [
        "PRAGMA journal_mode = WAL;",

        // NFR-R2: an abrupt power loss must not lose the last committed bill. FULL fsyncs the
        // WAL on every commit. Do NOT "optimise" this to NORMAL - NORMAL can lose the most
        // recent transactions on power loss, which for this shop means a bill that was taken,
        // printed and paid for is simply gone. This line is a durability requirement, not a
        // performance setting.
        "PRAGMA synchronous = FULL;",

        "PRAGMA foreign_keys = ON;",
        "PRAGMA busy_timeout = 5000;",
        "PRAGMA temp_store = MEMORY;",
        "PRAGMA cache_size = -20000;",
    ];

    /// <summary>
    /// SQLitePCLRaw's native provider must be selected once per process, before any connection
    /// is opened. Lazy gives us exactly-once with no double-checked locking of our own.
    /// </summary>
    private static readonly Lazy<bool> NativeProvider = new(
        () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lazy<string> _keyHex;
    private readonly string _connectionString;

    private SqliteConnection? _writeConnection;
    private bool _disposed;

    public PosConnectionFactory(PosDataDirectory dataDirectory, IDatabaseKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(keyStore);

        dataDirectory.EnsureCreated();
        DatabaseFilePath = dataDirectory.DatabaseFilePath;

        _keyHex = new Lazy<string>(() => ReadKeyAsHex(keyStore), LazyThreadSafetyMode.ExecutionAndPublication);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,

            // Pooling off on purpose: a recycled handle would skip the PRAGMA key and PRAGMA
            // block below, and there is no way to prove from the outside that it did not.
            Pooling = false,
        }.ToString();
    }

    public string DatabaseFilePath { get; }

    public async ValueTask<WriteConnectionLease> AcquireWriteConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _writeConnection ??= await OpenAsync(cancellationToken).ConfigureAwait(false);
            return new WriteConnectionLease(_writeGate, _writeConnection);
        }
        catch
        {
            _writeGate.Release();
            throw;
        }
    }

    public async Task<DbConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public DbConnection OpenConfiguredConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = NativeProvider.Value;

        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            ApplyKeyAndPragmas(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeConnection?.Dispose();
        _writeConnection = null;
        _writeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_writeConnection is not null)
        {
            await _writeConnection.DisposeAsync().ConfigureAwait(false);
            _writeConnection = null;
        }

        _writeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string ReadKeyAsHex(IDatabaseKeyStore keyStore)
    {
        var key = keyStore.GetOrCreateKey();
        if (key.Length != DatabaseKey.SizeInBytes)
        {
            throw new InvalidOperationException(
                $"The database key store returned {key.Length} bytes; " +
                $"SQLCipher needs {DatabaseKey.SizeInBytes}.");
        }

        return Convert.ToHexString(key);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        _ = NativeProvider.Value;

        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            ApplyKeyAndPragmas(connection);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void ApplyKeyAndPragmas(SqliteConnection connection)
    {
        // PRAGMA key must be the very first statement on the connection, before SQLite reads a
        // single page. The key is passed as x'<hex>' so SQLCipher takes it as raw key material
        // rather than a passphrase, and so no quoting or escaping question can arise.
        Execute(connection, "PRAGMA key = \"x'" + _keyHex.Value + "'\";");

        foreach (var pragma in ConnectionPragmas)
        {
            Execute(connection, pragma);
        }

        // Forces SQLCipher to decrypt page 1 here rather than at the first real query, so a
        // wrong key surfaces as "file is not a database" while opening instead of mid-sale.
        Execute(connection, "SELECT count(*) FROM sqlite_schema;");
    }
}
