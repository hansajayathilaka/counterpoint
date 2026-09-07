using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>How the customer is paying for one part of the bill (SRS FR-3.24, FR-3.25).</summary>
/// <param name="TenderType">A <see cref="TenderTypes"/> value.</param>
/// <param name="Amount">The amount taken by that means.</param>
/// <param name="Reference">
/// A short external reference. Never a card number (SRS NFR-S7).
/// </param>
public sealed record TenderRequest(string TenderType, Money Amount, string? Reference = null);
