using System;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// A clock that does not move unless a test moves it, so a <c>created_at</c> or a receipt file
/// name can be asserted on exactly.
/// </summary>
/// <remarks>
/// Only <see cref="GetUtcNow"/> is overridden. Timers fall through to the base implementation,
/// which is what the print worker's poll delay wants: a real, short wait.
/// </remarks>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    internal FixedTimeProvider(DateTimeOffset now) => _now = now;

    /// <summary>The offset <see cref="TimeProvider.GetLocalNow"/> reports.</summary>
    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    internal void Advance(TimeSpan by) => _now = _now.Add(by);
}
