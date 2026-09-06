using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Printing;

/// <summary>
/// The <c>print_job</c> outbox (SAD §8, CLAUDE.md invariant 7).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EnqueueAsync"/> joins the sale's transaction: the receipt becomes durable at the
/// same instant the bill does, so there is no window in which a bill exists with no receipt
/// queued. The other three methods are called by the print worker and each opens a transaction
/// of its own, which is what keeps the worker from holding one across a printer call.
/// </para>
/// <para>
/// <c>print_job</c> is not append-only, and deliberately so: status, attempts and the last
/// error are the record of what the printer did, and they change. What must never change is
/// the bill it refers to.
/// </para>
/// </remarks>
internal sealed class SqlitePrintJobOutbox : IPrintJobOutbox
{
    private const string Pending = "PENDING";
    private const string Printed = "PRINTED";
    private const string Failed = "FAILED";

    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SqlitePrintJobOutbox(SqliteUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<long> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new PrintJob
                {
                    DocType = request.DocType,
                    DocId = request.DocId,
                    Target = "RECEIPT",
                    Payload = request.Payload,
                    Copies = request.Copies,
                    IsDuplicate = request.IsDuplicate,
                    Status = Pending,
                    Attempts = 0,
                    LastError = null,
                    CreatedAt = _timeProvider.GetLocalNow(),
                    PrintedAt = null,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PendingPrintJob?> NextPendingAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<PendingPrintJob?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // Oldest first: receipts print in the order the bills were rung up, which is the
                // order the customers are standing in.
                var row = await context.Set<PrintJob>()
                    .Where(job => job.Status == Pending)
                    .OrderBy(job => job.Id)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false);

                return row is null
                    ? null
                    : new PendingPrintJob(row.Id, row.DocType, row.DocId, row.Payload, row.Copies, row.Attempts);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task MarkPrintedAsync(long printJobId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await FindAsync(context, printJobId, token).ConfigureAwait(false);
                if (row is not null)
                {
                    row.Status = Printed;
                    row.Attempts += 1;
                    row.LastError = null;
                    row.PrintedAt = _timeProvider.GetLocalNow();

                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task RecordFailedAttemptAsync(
        long printJobId,
        string failureReason,
        bool giveUp,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await FindAsync(context, printJobId, token).ConfigureAwait(false);
                if (row is not null)
                {
                    row.Attempts += 1;
                    row.LastError = failureReason;
                    row.Status = giveUp ? Failed : Pending;

                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return null;
            },
            cancellationToken);
    }

    private static Task<PrintJob?> FindAsync(PosDbContext context, long printJobId, CancellationToken token) =>
        context.Set<PrintJob>().FirstOrDefaultAsync(job => job.Id == printJobId, token);
}
