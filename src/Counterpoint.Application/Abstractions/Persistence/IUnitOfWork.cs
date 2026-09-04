using System;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One business operation, one local ACID transaction (CLAUDE.md "Not eventually consistent").
/// Implementations open the transaction with <c>BEGIN IMMEDIATE</c> on the single write
/// connection, so the writer lock is taken up front rather than half way through a bill.
/// </summary>
/// <remarks>
/// This is a port. The Application layer states what it needs; Counterpoint.Infrastructure
/// supplies the SQLite implementation.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction, committing on success and
    /// rolling back on any exception.
    /// </summary>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction, committing on success and
    /// rolling back on any exception.
    /// </summary>
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
