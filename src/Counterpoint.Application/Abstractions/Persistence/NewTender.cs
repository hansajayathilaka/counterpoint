using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One tender as it is actually recorded against a bill (SRS FR-3.24, FR-3.25) - what
/// <c>Counterpoint.Domain.Sales.TenderCalculator</c> applied, never what a cash tender ran over
/// by (SRS FR-3.26). These always sum to the bill total exactly; the handler computes that split
/// before anything is written rather than trying to correct one that does not.
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
