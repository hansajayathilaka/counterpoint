using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Audit;

/// <summary>
/// Appends to the hash-chained audit trail, in the caller's transaction
/// (CLAUDE.md invariants 5 and 6, SRS NFR-S8).
/// </summary>
/// <remarks>
/// The same chain construction as a bill's, deliberately: two tables, one definition of a link.
/// The entry is written inside the transaction it describes, so a rolled-back operation leaves
/// no audit row claiming it happened - and a committed one cannot leave the audit row behind.
/// </remarks>
internal sealed class SqliteAuditTrail : IAuditTrail
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteAuditTrail(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var previousHash = await context.Set<AuditLog>()
                    .OrderByDescending(row => row.Id)
                    .Select(row => row.RowHash)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false) ?? HashChain.GenesisHash;

                var row = new AuditLog
                {
                    OccurredAt = entry.OccurredAt,
                    UserId = entry.UserId,
                    Action = entry.Action,
                    EntityType = entry.EntityType,
                    EntityId = entry.EntityId,
                    BeforeJson = entry.BeforeJson,
                    AfterJson = entry.AfterJson,
                    Reason = entry.Reason,
                    PrevHash = previousHash,
                };

                row.RowHash = AuditLogHashChain.RowHash(previousHash, row);

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }
}
