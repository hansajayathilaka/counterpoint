using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Sales;

/// <summary>
/// One tender the cashier is offering against a bill, before it is checked (SRS FR-3.24,
/// FR-3.25).
/// </summary>
/// <param name="TenderType">
/// A <c>payment.tender_type</c> value, for example <c>CASH</c>. <see cref="TenderCalculator"/>
/// treats exactly one value specially (<see cref="TenderCalculator.Cash"/>); every other value is
/// opaque to it and is validated against the database's own <c>CHECK</c> constraint at the
/// storage boundary, not here.
/// </param>
/// <param name="Tendered">
/// What was actually offered. For cash this may be more than the bill still owes - that is what
/// makes change - but never for any other tender type (SRS FR-3.26).
/// </param>
/// <param name="Reference">
/// A short external reference, for example a card slip or cheque number. Never a card number in
/// full (SRS NFR-S7).
/// </param>
public sealed record TenderLine(string TenderType, Money Tendered, string? Reference = null);
