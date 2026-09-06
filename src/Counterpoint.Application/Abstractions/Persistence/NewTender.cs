using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One tender against a bill (SRS FR-3.24, FR-3.25). Tenders must sum to the bill total before
/// anything is written; the handler asserts that rather than silently correcting it.
/// </summary>
/// <param name="TenderType">One of the <c>payment.tender_type</c> values, for example <c>CASH</c>.</param>
/// <param name="Amount">The amount taken. Negative only for a refund out, which is P2's.</param>
/// <param name="Reference">
/// A short external reference. Never a card number: the column is length-capped and
/// PAN-rejecting (NFR-S7).
/// </param>
/// <param name="PaidAt">When it was taken.</param>
public sealed record NewTender(
    string TenderType,
    Money Amount,
    string? Reference,
    DateTimeOffset PaidAt);
