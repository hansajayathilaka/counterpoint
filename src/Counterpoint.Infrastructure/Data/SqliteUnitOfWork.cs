using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Runs one business operation as one <c>BEGIN IMMEDIATE</c> transaction on the single write
/// connection.
/// </summary>
/// <remarks>
/// <para>
/// IMMEDIATE, not DEFERRED: the writer lock is taken at BEGIN. A deferred transaction only
/// takes it at the first write, which means a busy database would be discovered half way
/// through a bill instead of before it started.
/// </para>
/// <para>
/// Re-entrant by design. The write gate is a <c>SemaphoreSlim(1,1)</c> and semaphores are not
/// re-entrant, so a use case that composes two of these ("post the sale, then post the stock
/// movement") would otherwise wait forever on a permit its own caller is holding - the sale
/// would hang, not fail (CLAUDE.md invariant 7). The innermost call therefore joins the
/// transaction already open on this async flow instead of starting a second one, which is also
/// the semantics the business rule wants: one business operation, one transaction.
/// </para>
/// </remarks>
public sealed class SqliteUnitOfWork : IUnitOfWork, IPosDbContextFactory
{
    private readonly IPosConnectionFactory _connectionFactory;

    /// <summary>
    /// The transaction open on the current async flow, if any. <see cref="AsyncLocal{T}"/> and
    /// not a plain field: concurrent callers each queue for their own lease at the gate, and one
    /// caller's open transaction must never be visible to another.
    /// </summary>
    private readonly AsyncLocal<AmbientTransaction?> _ambient = new();

    public SqliteUnitOfWork(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> against the write connection and its open transaction.
    /// Infrastructure repositories use this overload; the Application layer uses the
    /// <see cref="IUnitOfWork"/> ones.
    /// </summary>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<DbConnection, DbTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var ambient = _ambient.Value;
        if (ambient is not null)
        {
            // Already inside a unit of work on this flow. Join it: no second lease, no second
            // BEGIN, and no commit here - the outermost call owns the outcome, so a failure in
            // here still rolls the whole business operation back.
            return await operation(ambient.Connection, ambient.Transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        var lease = await _connectionFactory.AcquireWriteConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            var connection = (SqliteConnection)lease.Connection;
            var transaction = connection.BeginTransaction(deferred: false);

            await using (transaction.ConfigureAwait(false))
            {
                TResult result;

                _ambient.Value = new AmbientTransaction(connection, transaction);
                try
                {
                    result = await operation(connection, transaction, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    _ambient.Value = ambient;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInTransactionAsync(
            (_, _, token) => operation(token),
            cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public PosDbContext CreateDbContext()
    {
        var ambient = _ambient.Value ?? throw new InvalidOperationException(
            "A PosDbContext can only be created inside IUnitOfWork.ExecuteInTransactionAsync. " +
            "Opening one anywhere else would give EF Core a second connection to the same file, " +
            "outside the single-writer gate, and two writers is how this database gets corrupted.");

        var options = new DbContextOptionsBuilder<PosDbContext>()

            // contextOwnsConnection: false - the connection belongs to the factory's lease and
            // outlives this context. Disposing the context must not close the till's writer.
            .UseSqlite(ambient.Connection, contextOwnsConnection: false)
            .Options;

        var context = new PosDbContext(options);
        try
        {
            // Enlists EF in the BEGIN IMMEDIATE already running, so SaveChanges is part of the
            // same business transaction rather than committing on its own.
            context.Database.UseTransaction(ambient.Transaction);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Rolls back on the way out of a failed operation. The original exception is what the
    /// caller needs to see, so a rollback that fails because the transaction is already gone
    /// must not replace it.
    /// </summary>
    private static async Task RollbackQuietlyAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
        }
        catch (InvalidOperationException)
        {
            // Includes ObjectDisposedException: the transaction is already finished.
        }
    }

    /// <summary>The connection and transaction a nested call joins rather than reopening.</summary>
    private sealed record AmbientTransaction(DbConnection Connection, DbTransaction Transaction);
}
