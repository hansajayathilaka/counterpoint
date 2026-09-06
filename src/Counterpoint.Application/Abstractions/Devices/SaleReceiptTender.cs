using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>One printed tender line.</summary>
/// <param name="TenderType">A <c>payment.tender_type</c> value, for example <c>CASH</c>.</param>
/// <param name="Amount">The amount taken.</param>
public sealed record SaleReceiptTender(string TenderType, Money Amount);
