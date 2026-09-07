using System;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of the audit trail (SRS NFR-S8). Append-only and hash chained, so an entry is
/// evidence rather than a note.
/// </summary>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="UserId">Who did it. Null only for something the system did unattended.</param>
/// <param name="Action">What happened, for example <c>SALE_COMPLETED</c>.</param>
/// <param name="EntityType">The table it happened to, for example <c>sale</c>.</param>
/// <param name="EntityId">The row it happened to.</param>
/// <param name="BeforeJson">State before, for a change. Null for a creation.</param>
/// <param name="AfterJson">State after.</param>
/// <param name="Reason">The reason given, where one is required (owner overrides, cancellations).</param>
public sealed record AuditEntry(
    DateTimeOffset OccurredAt,
    long? UserId,
    string Action,
    string EntityType,
    long? EntityId,
    string? BeforeJson = null,
    string? AfterJson = null,
    string? Reason = null);
