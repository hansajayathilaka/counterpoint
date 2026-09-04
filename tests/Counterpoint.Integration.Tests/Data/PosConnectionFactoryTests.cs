using System;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// Cover for the encrypted database bootstrap (P0-T03, NFR-R2, NFR-S3).
/// </summary>
public sealed class PosConnectionFactoryTests
{
    /// <summary>SQLITE_NOTADB. What a reader without the key gets: undecryptable page 1.</summary>
    private const int SqliteNotADatabase = 26;

    /// <summary>SQLITE_BUSY: another connection holds the write lock.</summary>
    private const int SqliteBusy = 5;

    /// <summary>
    /// The busy wait every connection promises, in seconds: <c>PRAGMA busy_timeout = 5000</c>
    /// (engineering guide §4.8, CLAUDE.md invariant 9).
    /// </summary>
    private const int BusyTimeoutSeconds = 5;

    /// <summary>
    /// A contended write must give up below this. Well clear of the 5 s it should take, and well
    /// below Microsoft.Data.Sqlite's 30 s default retry budget, so the two cannot be confused.
    /// </summary>
    private static readonly TimeSpan ContendedWriteCeiling = TimeSpan.FromSeconds(15);

    /// <summary>
    /// And not below this: giving up early would mean the connection waits less than the 5000 ms
    /// the pragma promises, which is its own bug.
    /// </summary>
    private static readonly TimeSpan ContendedWriteFloor = TimeSpan.FromSeconds(4);

    /// <summary>Long enough that a merely slow continuation would have run by now.</summary>
    private static readonly TimeSpan BlockedProbe = TimeSpan.FromMilliseconds(250);

    /// <summary>Bounds a wait that must succeed, so a regression fails the test instead of hanging it.</summary>
    private static readonly TimeSpan MustSucceedWithin = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DatabaseIsCreatedClosedReopenedAndReadWithTheStoredKey()
    {
        using var fixture = new TemporaryDataDirectory();

        await using (var factory = fixture.CreateConnectionFactory())
        {
            await using var lease = await factory.AcquireWriteConnectionAsync();
            await ExecuteAsync(lease.Connection, "CREATE TABLE smoke (note TEXT NOT NULL);");
            await ExecuteAsync(lease.Connection, "INSERT INTO smoke (note) VALUES ('one till, one database');");
        }

        File.Exists(fixture.DataDirectory.DatabaseFilePath)
            .Should().BeTrue("the database file is the single source of truth and must be on disk");

        // A brand new factory: new process in all but name, same key store, same file.
        await using var reopened = fixture.CreateConnectionFactory();
        await using var connection = await reopened.OpenReadConnectionAsync();

        var note = await ScalarAsync(connection, "SELECT note FROM smoke;");

        note.Should().Be("one till, one database");
    }

    [Fact]
    public async Task NFR_S3_OpeningWithTheWrongKeyFails()
    {
        using var fixture = new TemporaryDataDirectory();

        await using (var factory = fixture.CreateConnectionFactory())
        {
            await using var lease = await factory.AcquireWriteConnectionAsync();
            await ExecuteAsync(lease.Connection, "CREATE TABLE smoke (note TEXT NOT NULL);");
        }

        // A plain SQLite tool identifies a database by the 16-byte "SQLite format 3\0" header.
        // SQLCipher encrypts page 1 including that header, so the tool sees only noise.
        var header = new byte[16];
        using (var file = File.OpenRead(fixture.DataDirectory.DatabaseFilePath))
        {
            file.ReadExactly(header);
        }

        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");

        var wrongKey = new byte[DatabaseKey.SizeInBytes];
        Array.Fill(wrongKey, (byte)0x2A);

        await using var impostor = fixture.CreateConnectionFactory(new FixedKeyStore(wrongKey));

        var open = async () => await impostor.OpenReadConnectionAsync();

        var thrown = await open.Should().ThrowAsync<SqliteException>(
            "an encrypted database must be unreadable without its key");

        thrown.Which.SqliteErrorCode.Should().Be(
            SqliteNotADatabase,
            "the wrong key must fail as \"file is not a database\" while opening, not as an " +
            "empty result set that a caller could mistake for a fresh till");
    }

    /// <summary>
    /// The other half of the "opening it with a plain SQLite tool fails" checkbox: not the
    /// header bytes, and not this process's own SQLCipher-linked provider, but the stock
    /// <c>sqlite3</c> binary run against the file the till actually leaves on disk.
    /// </summary>
    [Fact]
    public async Task NFR_S3_APlainSqliteToolCannotOpenTheDatabase()
    {
        SqliteCli.ExecutablePath.Should().NotBeNull(
            "this test needs the stock sqlite3 binary on PATH; CI installs it and so must a " +
            "development machine (apt-get install sqlite3)");

        using var fixture = new TemporaryDataDirectory();

        await using (var factory = fixture.CreateConnectionFactory())
        {
            await using var lease = await factory.AcquireWriteConnectionAsync();
            await ExecuteAsync(lease.Connection, "CREATE TABLE takings (note TEXT NOT NULL);");
            await ExecuteAsync(lease.Connection, "INSERT INTO takings (note) VALUES ('till float 5000');");
        }

        var databasePath = fixture.DataDirectory.DatabaseFilePath;

        // 1. The tool cannot even list the tables.
        var tables = SqliteCli.Run(databasePath, ".tables");
        tables.ExitCode.Should().NotBe(0, "a stock SQLite tool must not be able to read the schema");
        tables.AllOutput.Should().Contain("file is not a database");
        tables.StandardOutput.Should().NotContain("takings");

        // 2. It cannot check the file's integrity, because it cannot decrypt page 1.
        var integrity = SqliteCli.Run(databasePath, "PRAGMA integrity_check;");
        integrity.ExitCode.Should().NotBe(0);
        integrity.AllOutput.Should().Contain("file is not a database");
        integrity.StandardOutput.Should().NotContain("ok");

        // 3. And it cannot reach the data, which is the point.
        var query = SqliteCli.Run(databasePath, "SELECT note FROM takings;");
        query.ExitCode.Should().NotBe(0);
        query.StandardOutput.Should().NotContain("till float 5000");

        // The plaintext is not lying around in the file either.
        var raw = await File.ReadAllBytesAsync(databasePath);
        Encoding.UTF8.GetString(raw).Should().NotContain(
            "till float 5000",
            "SQLCipher must encrypt the page contents, not merely the header");

        // The tool was run read-only, so the till's own database must still open afterwards.
        await using var reopened = fixture.CreateConnectionFactory();
        await using var connection = await reopened.OpenReadConnectionAsync();
        (await ScalarAsync(connection, "SELECT note FROM takings;")).Should().Be("till float 5000");
    }

    [Fact]
    public async Task NFR_R2_EveryConnectionCarriesTheRequiredPragmas()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await using (var read = await factory.OpenReadConnectionAsync())
        {
            await AssertRequiredPragmasAsync(read);
        }

        // "Every connection, without exception" (engineering guide §4.8) includes the long-lived
        // write connection, which is the one that matters for durability.
        await using var lease = await factory.AcquireWriteConnectionAsync();
        await AssertRequiredPragmasAsync(lease.Connection);
    }

    /// <summary>
    /// <c>PRAGMA busy_timeout</c> is only half the dial. On top of SQLite's own busy handler,
    /// Microsoft.Data.Sqlite runs a busy/locked retry loop bounded by the command timeout, which
    /// defaults to 30 seconds; whichever is longer is what the caller actually waits. A till that
    /// blocks for half a minute on a contended write is a till that has blocked the sale
    /// (CLAUDE.md invariant 7), so the connection string's <c>Default Timeout</c> must agree with
    /// the pragma. Proved from the outside with a second factory over the same file, standing in
    /// for a backup job or a diagnostic tool.
    /// </summary>
    [Fact]
    public async Task AContendedWriteGivesUpAtTheBusyTimeoutNotAtAdoNetsDefault()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await using var lease = await factory.AcquireWriteConnectionAsync();
        await ExecuteAsync(lease.Connection, "CREATE TABLE smoke (note TEXT NOT NULL);");

        await using var rivalFactory = fixture.CreateConnectionFactory();
        await using var rival = await rivalFactory.OpenReadConnectionAsync();

        // Held for the duration of the rival's attempt: BEGIN IMMEDIATE takes the writer lock now.
        var transaction = ((SqliteConnection)lease.Connection).BeginTransaction(deferred: false);
        await using (transaction.ConfigureAwait(false))
        {
            var rivalWrite = async () =>
                await ExecuteAsync(rival, "INSERT INTO smoke (note) VALUES ('theirs');");

            var clock = Stopwatch.StartNew();
            var thrown = await rivalWrite.Should().ThrowAsync<SqliteException>();
            clock.Stop();

            thrown.Which.SqliteErrorCode.Should().Be(SqliteBusy);

            clock.Elapsed.Should().BeLessThan(
                ContendedWriteCeiling,
                "a blocked write must give up after the 5000 ms busy_timeout, not after " +
                "Microsoft.Data.Sqlite's 30-second command-timeout retry budget");

            clock.Elapsed.Should().BeGreaterThan(
                ContendedWriteFloor,
                "and it must actually wait out the busy_timeout first, rather than failing the " +
                "moment it sees the lock");

            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task WriteConnectionIsSerialisedBehindTheGate()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        var lease = await factory.AcquireWriteConnectionAsync();

        var second = factory.AcquireWriteConnectionAsync().AsTask();

        // Not merely "has not completed yet": still blocked after a real wait.
        var raced = await Task.WhenAny(second, Task.Delay(BlockedProbe));
        raced.Should().NotBeSameAs(second, "only one writer may hold the connection at a time");
        second.IsCompleted.Should().BeFalse();

        await lease.DisposeAsync();

        var granted = await second.WaitAsync(MustSucceedWithin);
        granted.Connection.Should().BeSameAs(lease.Connection, "there is exactly one write connection");
        await granted.DisposeAsync();
    }

    /// <summary>
    /// A lease that is disposed twice must not hand out a second permit, or two callers would
    /// end up writing down the same connection at once.
    /// </summary>
    [Fact]
    public async Task DisposingALeaseTwiceDoesNotReleaseTheGateTwice()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        var lease = await factory.AcquireWriteConnectionAsync();
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        var held = await factory.AcquireWriteConnectionAsync().AsTask().WaitAsync(MustSucceedWithin);

        var second = factory.AcquireWriteConnectionAsync().AsTask();
        var raced = await Task.WhenAny(second, Task.Delay(BlockedProbe));
        raced.Should().NotBeSameAs(second, "the double dispose must not have added a permit");

        await held.DisposeAsync();
        await (await second.WaitAsync(MustSucceedWithin)).DisposeAsync();
    }

    [Fact]
    public async Task UsingADisposedFactoryFailsLoudlyRatherThanReopeningTheDatabase()
    {
        using var fixture = new TemporaryDataDirectory();
        var factory = fixture.CreateConnectionFactory();

        await using (var lease = await factory.AcquireWriteConnectionAsync())
        {
            await ExecuteAsync(lease.Connection, "CREATE TABLE smoke (note TEXT NOT NULL);");
        }

        await factory.DisposeAsync();

        var write = async () => await factory.AcquireWriteConnectionAsync();
        var read = async () => await factory.OpenReadConnectionAsync();

        await write.Should().ThrowAsync<ObjectDisposedException>();
        await read.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static async Task AssertRequiredPragmasAsync(DbConnection connection)
    {
        (await ScalarAsync(connection, "PRAGMA journal_mode;")).Should().Be("wal");

        // 2 == FULL. NFR-R2 durability; NORMAL would lose the last committed bill on power loss.
        (await ScalarAsync(connection, "PRAGMA synchronous;")).Should().Be("2");

        (await ScalarAsync(connection, "PRAGMA foreign_keys;")).Should().Be("1");
        (await ScalarAsync(connection, "PRAGMA busy_timeout;")).Should().Be("5000");

        // The pragma is not the effective wait on its own: Microsoft.Data.Sqlite runs its own
        // busy retry loop bounded by the command timeout, and the caller waits the longer of the
        // two. Default Timeout must therefore match busy_timeout, in seconds.
        ((SqliteConnection)connection).DefaultTimeout.Should().Be(
            BusyTimeoutSeconds,
            "Default Timeout and PRAGMA busy_timeout must agree, or ADO.NET's 30-second retry " +
            "budget silently overrides the pragma's promise - see " +
            nameof(AContendedWriteGivesUpAtTheBusyTimeoutNotAtAdoNetsDefault));

        // The remaining two from engineering guide §4.8. temp_store 2 == MEMORY; a negative
        // cache_size is a size in KiB rather than a page count.
        (await ScalarAsync(connection, "PRAGMA temp_store;")).Should().Be("2");
        (await ScalarAsync(connection, "PRAGMA cache_size;")).Should().Be("-20000");
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
