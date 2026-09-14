using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One <c>credit_note</c> row, as a lookup by number or customer needs it (task P2-T05
/// step 3, SRS FR-5 store credit).</summary>
/// <remarks>
/// No cost or margin field - a credit note carries none to exclude (CLAUDE.md invariant 8 has
/// nothing to say here). Reading one by number or by customer carries no role requirement of its
/// own for the same reason <c>IProductLookup</c> does not: it is the till asking "does this
/// customer have store credit", not an owner-only figure (docs/Counterpoint_Requirements.md FR-5).
/// </remarks>
public sealed record CreditNoteRecord(
    long Id,
    string Number,
    long SaleReturnId,
    long? CustomerId,
    Money AmountIssued,
    Money AmountRemaining,
    DateTimeOffset IssuedAt,
    DateOnly? ExpiresOn,
    string Status);
