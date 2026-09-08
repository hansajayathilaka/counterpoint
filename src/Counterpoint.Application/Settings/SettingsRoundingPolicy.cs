using System;
using System.Threading;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings;

/// <summary>
/// The shop's rounding rule, taken from settings on every use (SRS FR-10.2).
/// </summary>
/// <remarks>
/// <para>
/// This is what makes "changing the currency's decimal places changes displayed and printed
/// amounts without a restart" true. A policy built once in the composition root would have
/// captured the decimal places as they were at start-up; this reads them from
/// <see cref="ISettings"/> each time, so the very next line total is rounded the new way.
/// </para>
/// <para>
/// It is still one rounding policy, applied at exactly two points - the line total and the bill
/// total (CLAUDE.md invariant 2). Nothing about reading the setting late changes where rounding
/// happens.
/// </para>
/// <para>
/// The built policy is memoised against the rule and places it was built from, so the common
/// case - nothing has changed since the last line - is a field read and a comparison rather than
/// an allocation per amount.
/// </para>
/// </remarks>
public sealed class SettingsRoundingPolicy : IRoundingPolicy
{
    private readonly ISettings _settings;
    private Cached? _cached;

    public SettingsRoundingPolicy(ISettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public int DecimalPlaces => Policy().DecimalPlaces;

    /// <inheritdoc />
    public Money Round(Money amount) => Policy().Round(amount);

    private IRoundingPolicy Policy()
    {
        var financial = _settings.Financial;
        var cached = Volatile.Read(ref _cached);

        if (cached is not null
            && cached.Rule == financial.RoundingRule
            && cached.DecimalPlaces == financial.DecimalPlaces)
        {
            return cached.Policy;
        }

        // A benign race: two threads may both build one, and both are identical. Publishing an
        // immutable record means a reader sees either the old policy or the new one, never a
        // half-built one.
        var rebuilt = new Cached(
            financial.RoundingRule,
            financial.DecimalPlaces,
            RoundingPolicyFactory.Create(financial.RoundingRule, financial.DecimalPlaces));

        Volatile.Write(ref _cached, rebuilt);
        return rebuilt.Policy;
    }

    private sealed record Cached(RoundingRule Rule, int DecimalPlaces, IRoundingPolicy Policy);
}
