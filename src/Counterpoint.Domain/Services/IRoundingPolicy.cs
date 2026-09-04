using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Services;

/// <summary>
/// How the shop turns a computed amount into an amount it can actually take money for
/// (SRS FR-10.2: decimal places and rounding rule are configurable settings).
///
/// Rounding happens at exactly two points — the line total and the bill total — and always
/// through this interface (CLAUDE.md invariant 2). If a calculation seems to need a third
/// rounding point, the calculation is structured wrong.
/// </summary>
public interface IRoundingPolicy
{
    /// <summary>
    /// The currency's decimal places, from settings. Two for most currencies; zero for a
    /// currency with no minor unit.
    /// </summary>
    public int DecimalPlaces { get; }

    /// <summary>Rounds an amount to <see cref="DecimalPlaces"/>.</summary>
    public Money Round(Money amount);
}
