using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Sales;

/// <summary>
/// One tender as it is actually recorded against a bill: never more than the bill still owed at
/// the moment it was taken (SRS FR-3.24, FR-3.25, FR-3.26).
/// </summary>
/// <remarks>
/// This is what becomes a <c>payment</c> row. A cash tender that overpaid is capped here; the
/// difference is <see cref="TenderPlan.Change"/>, not a second <see cref="AppliedTender"/> - the
/// change is handed back across the counter, never owed and never banked.
/// </remarks>
/// <param name="TenderType">A <c>payment.tender_type</c> value.</param>
/// <param name="Amount">What this tender actually pays off the bill.</param>
/// <param name="Reference">Carried through from the offered <see cref="TenderLine"/>, unchanged.</param>
public sealed record AppliedTender(string TenderType, Money Amount, string? Reference);
