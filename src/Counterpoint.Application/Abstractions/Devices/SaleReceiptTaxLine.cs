using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// One row of the bill's tax breakdown - every distinct tax rate its lines were charged at, with
/// the net amount that rate applied to and the tax it produced (SRS §10.1's "Tax @ n%" row,
/// FR-10.3).
/// </summary>
/// <param name="Label">Printable label, for example <c>Tax @ 15%</c>.</param>
/// <param name="TaxableAmount">The net line total this rate was charged on.</param>
/// <param name="TaxAmount">The tax that rate produced.</param>
public sealed record SaleReceiptTaxLine(string Label, Money TaxableAmount, Money TaxAmount);
