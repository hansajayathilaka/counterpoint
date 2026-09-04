using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Runs one business operation as one <c>BEGIN IMMEDIATE</c> transaction on the single write
/// connection.
/// </summary>
/// <remarks>
/// IMMEDIATE, not DEFERRED: the writer lock is taken at BEGIN. A deferred transaction only
/// takes it at the first write, which means a busy database would be discovered half way
/// through a bill instead of before it started.
/// </remarks>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly IPosConnectionFactory _connectionFactory;

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

        var lease = await _connectionFactory.AcquireWriteConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            var connection = (SqliteConnection)lease.Connection;
            var transaction = connection.BeginTransaction(deferred: false);

            await using (transaction.ConfigureAwait(false))
            {
                TResult result;
                try
                {
                    result = await operation(connection, transaction, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                    throw;
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
}
