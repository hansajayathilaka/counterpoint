using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Sales;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Sales;

/// <summary><c>held_bill</c>, read and written through the unit of work (SRS FR-3.32, FR-3.33, P1-T09).</summary>
internal sealed class SqliteHeldBillService : IHeldBillService
{
    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SqliteHeldBillService(SqliteUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<long> HoldAsync(
        string label,
        HeldBillPayload payload,
        long userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(payload);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new HeldBill
                {
                    Label = label.Trim(),
                    Payload = JsonSerializer.Serialize(payload),
                    CreatedAt = _timeProvider.GetLocalNow(),
                    UserId = userId,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HeldBillSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<HeldBill>()
                    .OrderByDescending(row => row.CreatedAt)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<HeldBillSummary> result =
                    [.. rows.Select(row => new HeldBillSummary(row.Id, row.Label, row.CreatedAt, CountLines(row.Payload)))];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<HeldBillPayload> RecallAsync(long heldBillId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<HeldBill>().FirstOrDefaultAsync(r => r.Id == heldBillId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"There is no held bill with id {heldBillId}. It may already have been recalled.");

                var payload = JsonSerializer.Deserialize<HeldBillPayload>(row.Payload)
                    ?? throw new InvalidOperationException(
                        $"Held bill {heldBillId}'s payload could not be read back.");

                // Recalled, not merely read: a held bill is not evidence of a transaction the way
                // a completed sale is, so it does not linger once the cashier has it back on
                // screen (unlike sale/sale_line, held_bill carries no append-only trigger).
                context.Remove(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return payload;
            },
            cancellationToken);

    private static int CountLines(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<HeldBillPayload>(payload)?.Lines.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
