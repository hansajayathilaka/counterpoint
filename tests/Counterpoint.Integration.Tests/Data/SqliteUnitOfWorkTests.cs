using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// One business operation, one local ACID transaction, opened with BEGIN IMMEDIATE.
/// </summary>
public sealed class SqliteUnitOfWorkTests
{
    /// <summary>SQLITE_BUSY: another connection holds the write lock.</summary>
    private const int SqliteBusy = 5;

    /// <summary>
    /// The rival connection's busy_timeout while a contention test runs. Short so a blocked
    /// write fails in milliseconds instead of sitting on the §4.8 default of five seconds.
    /// </summary>
    private const int RivalBusyTimeoutMilliseconds = 100;

    /// <summary>
    /// Microsoft.Data.Sqlite's own busy retry loop is bounded by the command timeout, not by
    /// <c>PRAGMA busy_timeout</c>, and defaults to 30 seconds. One second is the floor that
    /// still means "give up".
    /// </summary>
    private const int RivalCommandTimeoutSeconds = 1;

    private static readonly TimeSpan MustSucceedWithin = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CommitsOnSuccess()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            await ExecuteAsync(connection, transaction, "INSERT INTO smoke (note) VALUES ('kept');", token);
            return 0;
        });

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(1);
    }

    [Fact]
    public async Task RollsBackOnFailureAndReleasesTheWriteGate()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            return 0;
        });

        var failing = async () => await unitOfWork.ExecuteInTransactionAsync<int>(
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO smoke (note) VALUES ('lost');", token);
                throw new InvalidOperationException("the shop said no");
            });

        await failing.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("the shop said no", "the caller must see its own failure, not a rollback error");

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(0);

        // The gate must be free again, or the next bill would hang forever. Bounded, so a
        // regression fails this test rather than hanging the whole run.
        var lease = await factory.AcquireWriteConnectionAsync().AsTask().WaitAsync(MustSucceedWithin);
        await using (lease.ConfigureAwait(false))
        {
            // The connection must also be usable, not left inside an abandoned transaction.
            await ExecuteAsync(lease.Connection, null, "INSERT INTO smoke (note) VALUES ('after');", default);
        }

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(1);
    }

    /// <summary>
    /// The writer lock must be taken at BEGIN, not at the first write. Proved from the outside:
    /// a second connection's write is refused while a unit of work that has not written a single
    /// row is open. Under DEFERRED no lock would be held at that moment and the rival would win -
    /// see <see cref="ADeferredTransactionWouldNotHaveBlockedTheRival"/>, the control for this.
    /// </summary>
    [Fact]
    public async Task BeginsImmediateSoTheWriteLockIsHeldBeforeTheFirstWrite()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            return 0;
        });

        // A separate factory over the same file: a second process in all but name. Its own
        // write gate is unrelated to ours, so the only thing that can stop it is SQLite.
        await using var rivalFactory = fixture.CreateConnectionFactory();
        await using var rival = await OpenImpatientRivalAsync(rivalFactory);

        var transactionOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rivalFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var work = unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            // Deliberately nothing written yet: this is the exact window in which a DEFERRED
            // transaction holds no lock at all.
            transactionOpen.SetResult();
            await rivalFinished.Task;

            await ExecuteAsync(connection, transaction, "INSERT INTO smoke (note) VALUES ('mine');", token);
            return 0;
        });

        await transactionOpen.Task.WaitAsync(MustSucceedWithin);

        var rivalWrite = async () =>
            await ExecuteAsync(rival, null, "INSERT INTO smoke (note) VALUES ('theirs');", default);

        try
        {
            var thrown = await rivalWrite.Should().ThrowAsync<SqliteException>(
                "BEGIN IMMEDIATE takes the writer lock at BEGIN, so a second writer is refused " +
                "before the unit of work has written anything");

            thrown.Which.SqliteErrorCode.Should().Be(SqliteBusy);
        }
        finally
        {
            rivalFinished.SetResult();
        }

        await work.WaitAsync(MustSucceedWithin);

        // Ours committed; theirs never happened.
        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(1);
        (await ScalarAsync(factory, "SELECT note FROM smoke;")).Should().Be("mine");
    }

    /// <summary>
    /// Control for <see cref="BeginsImmediateSoTheWriteLockIsHeldBeforeTheFirstWrite"/>. Same
    /// file, same rival, same window - only the deferred flag differs, and the rival now
    /// succeeds. Without this, the test above could be passing for some ambient reason and
    /// nobody would know if <c>deferred: false</c> were flipped to true.
    /// </summary>
    [Fact]
    public async Task ADeferredTransactionWouldNotHaveBlockedTheRival()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            return 0;
        });

        await using var rivalFactory = fixture.CreateConnectionFactory();
        await using var rival = await OpenImpatientRivalAsync(rivalFactory);

        await using var lease = await factory.AcquireWriteConnectionAsync();
        var deferred = ((SqliteConnection)lease.Connection).BeginTransaction(deferred: true);

        await using (deferred.ConfigureAwait(false))
        {
            var rivalWrite = async () =>
                await ExecuteAsync(rival, null, "INSERT INTO smoke (note) VALUES ('theirs');", default);

            await rivalWrite.Should().NotThrowAsync(
                "a deferred transaction holds no writer lock until its first write, which is " +
                "precisely why the unit of work must not use one");

            await deferred.RollbackAsync();
        }

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(1);
    }

    /// <summary>
    /// The gate is what keeps concurrent callers off SQLite's busy-retry path (engineering
    /// guide §4.8). Many callers at once must all land, in some order, with nobody refused.
    /// </summary>
    [Fact]
    public async Task ConcurrentUnitsOfWorkAreSerialisedAndAllCommit()
    {
        const int Writers = 32;

        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (n INTEGER NOT NULL);", token);
            return 0;
        });

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writers = Enumerable.Range(0, Writers).Select(async n =>
        {
            await start.Task;
            await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
            {
                var current = await ScalarAsync(connection, transaction, "SELECT count(*) FROM smoke;", token);

                // Read-then-write inside the transaction: if two of these ever overlapped, the
                // count would be wrong rather than merely the row order being different.
                await ExecuteAsync(
                    connection,
                    transaction,
                    $"INSERT INTO smoke (n) VALUES ({current});",
                    token);
                return n;
            });
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(writers).WaitAsync(MustSucceedWithin);

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(Writers);

        // Each writer saw a distinct count, so no two transactions ran against the same state.
        var observed = await ReadAllAsync(factory, "SELECT n FROM smoke ORDER BY n;");
        observed.Should().BeEquivalentTo(
            Enumerable.Range(0, Writers).Select(n => (long)n),
            "serialised writers must each observe the previous one's commit");
    }

    /// <summary>
    /// The write gate is a <c>SemaphoreSlim(1,1)</c> and semaphores are not re-entrant. A use
    /// case that composes two units of work - post the sale, then post the stock movement - would
    /// queue for a permit its own caller is holding and wait forever: the sale would hang, and
    /// nothing must ever block the sale (CLAUDE.md invariant 7).
    /// </summary>
    /// <remarks>
    /// Bounded with <c>WaitAsync</c> so a regression fails this test in seconds instead of
    /// hanging the whole run with no output.
    /// </remarks>
    [Fact]
    public async Task ANestedUnitOfWorkDoesNotDeadlockOnTheWriteGate()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        var work = unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);

            await unitOfWork.ExecuteInTransactionAsync(async inner =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO smoke (note) VALUES ('inner');", inner);
            }, token);

            return 0;
        });

        await work.WaitAsync(MustSucceedWithin);

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(1);
    }

    /// <summary>
    /// The nested call joins the open transaction rather than starting a second one, so the whole
    /// composed operation is still one ACID transaction: an inner write must be rolled back by an
    /// outer failure, not left committed behind it.
    /// </summary>
    [Fact]
    public async Task ANestedUnitOfWorkJoinsTheOpenTransactionRatherThanCommittingSeparately()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            return 0;
        });

        var failing = async () => await unitOfWork.ExecuteInTransactionAsync<int>(
            async (outerConnection, outerTransaction, token) =>
            {
                await unitOfWork.ExecuteInTransactionAsync(
                    async (innerConnection, innerTransaction, innerToken) =>
                    {
                        // Same connection and same transaction: no second BEGIN happened.
                        innerConnection.Should().BeSameAs(outerConnection);
                        innerTransaction.Should().BeSameAs(outerTransaction);

                        await ExecuteAsync(
                            innerConnection,
                            innerTransaction,
                            "INSERT INTO smoke (note) VALUES ('inner');",
                            innerToken);
                        return 0;
                    },
                    token);

                throw new InvalidOperationException("the shop said no");
            }).WaitAsync(MustSucceedWithin);

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("the shop said no");

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(
            0,
            "the inner write is part of the outer transaction, so the outer failure undoes it");
    }

    /// <summary>
    /// A rival connection standing in for a second process, made impatient so that a blocked
    /// write fails in about a second rather than waiting out a retry budget.
    /// </summary>
    /// <remarks>
    /// Two dials, not one. <c>PRAGMA busy_timeout</c> governs SQLite's own retry; on top of it
    /// Microsoft.Data.Sqlite runs its own busy/locked retry loop bounded by the command timeout,
    /// which defaults to 30 seconds. Lowering only the pragma leaves the caller waiting for the
    /// ADO.NET loop instead.
    /// </remarks>
    private static async Task<DbConnection> OpenImpatientRivalAsync(PosConnectionFactory factory)
    {
        var connection = (SqliteConnection)await factory.OpenReadConnectionAsync();
        connection.DefaultTimeout = RivalCommandTimeoutSeconds;

        await ExecuteAsync(
            connection,
            null,
            $"PRAGMA busy_timeout = {RivalBusyTimeoutMilliseconds};",
            default);

        return connection;
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<long> CountAsync(PosConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenReadConnectionAsync();
        return await ScalarAsync(connection, null, sql, default);
    }

    private static async Task<string?> ScalarAsync(PosConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenReadConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync()) as string;
    }

    private static async Task<long[]> ReadAllAsync(PosConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenReadConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetInt64(0));
        }

        return [.. values];
    }
}
