using Counterpoint.Domain.Services;

namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.2 - the currency, how it is written, and how amounts are rounded.
/// </summary>
/// <param name="CurrencyCode">ISO 4217 code, for exports and reports.</param>
/// <param name="CurrencySymbol">The symbol printed and displayed beside an amount.</param>
/// <param name="SymbolPosition">Whether the symbol leads or trails the amount.</param>
/// <param name="DecimalPlaces">
/// The currency's minor digits. Drives <see cref="IRoundingPolicy.DecimalPlaces"/> and every
/// displayed and printed amount, so changing it changes both, without a restart.
/// </param>
/// <param name="RoundingRule">Which way a midpoint goes at the two rounding points.</param>
/// <param name="QuantityDecimalPlaces">
/// How many decimals a quantity is shown to. A UOM may allow fewer; this is the ceiling the
/// till displays and prints at.
/// </param>
public sealed record FinancialSettings(
    string CurrencyCode,
    string CurrencySymbol,
    CurrencySymbolPosition SymbolPosition,
    int DecimalPlaces,
    RoundingRule RoundingRule,
    int QuantityDecimalPlaces);
