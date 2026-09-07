using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Appends to the tamper-evident audit trail (SRS NFR-S8, CLAUDE.md invariants 5 and 6).
/// </summary>
/// <remarks>
/// There is deliberately no read, no update and no delete on this port. The entry is written
/// inside the business transaction it describes, so an operation that rolls back leaves no
/// audit row claiming it happened. The chaining is the implementation's business for the same
/// reason it is on <see cref="ISaleWriter"/>: a caller that chose its own hash could break the
/// chain.
/// </remarks>
public interface IAuditTrail
{
    /// <summary>Appends one entry, in the caller's transaction.</summary>
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
