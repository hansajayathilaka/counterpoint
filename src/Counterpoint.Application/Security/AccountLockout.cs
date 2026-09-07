using System;

namespace Counterpoint.Application.Security;

/// <summary>
/// The rate limit on failed sign-ins (SRS NFR-S9).
/// </summary>
/// <remarks>
/// <para>
/// <b>NFR-S9 says "logged and rate-limited after 5 consecutive failures" and stops there, so the
/// shape of the limit is a decision this task had to make. It is:</b>
/// </para>
/// <code>
/// lockout(failures) = null                                             for failures &lt; 5
///                   = min(30s * 2^(failures - 5), 15 minutes)          for failures >= 5
/// </code>
/// <para>
/// which gives 30 s at the fifth failure, then 1, 2, 4, 8 and 15 minutes, held at 15 from the
/// tenth failure onward. A correct password resets the counter to zero.
/// </para>
/// <para>
/// <b>Why bounded and not permanent.</b> This is a one-till shop with no second terminal and no
/// administrator down the corridor. A cashier who fat-fingers a PIN five times before the morning
/// rush must not need the owner to drive in, and a permanent lock on the only owner account would
/// take the shop's own database away from it. The ceiling is the point where the lock stops being
/// a security control and starts being an outage.
/// </para>
/// <para>
/// <b>Why it is still enough.</b> Fifteen minutes caps an online guesser at about four attempts
/// an hour, which is hopeless against even a four-digit PIN, and an attacker with the file rather
/// than the keyboard is up against Argon2id at 64 MB a guess instead (see
/// <see cref="Argon2Parameters"/>). An owner who wants a locked account back immediately resets
/// its password, which clears the counter.
/// </para>
/// <para>
/// <b>Attempts made while locked do not extend the lock.</b> Counting them would let anyone at
/// the counter keep the till's own staff out indefinitely by typing rubbish, which is a denial of
/// service dressed up as a security control.
/// </para>
/// </remarks>
public static class AccountLockout
{
    /// <summary>Consecutive failures before the account locks (SRS NFR-S9).</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>The lock applied at the fifth consecutive failure.</summary>
    public static TimeSpan BaseDuration => TimeSpan.FromSeconds(30);

    /// <summary>The longest the backoff is ever allowed to grow to.</summary>
    public static TimeSpan MaximumDuration => TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long the account is locked after <paramref name="consecutiveFailures"/> failures in a
    /// row, or null when it is not locked yet.
    /// </summary>
    public static TimeSpan? DurationFor(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        if (consecutiveFailures < MaxFailedAttempts)
        {
            return null;
        }

        var doublings = consecutiveFailures - MaxFailedAttempts;

        // Capped before the shift, not after it: 30 seconds doubled 60 times overflows a long,
        // and an overflow here would hand back a negative lockout - an account that unlocks
        // itself the more it is attacked.
        if (doublings >= 32)
        {
            return MaximumDuration;
        }

        var duration = TimeSpan.FromTicks(BaseDuration.Ticks << doublings);

        return duration > MaximumDuration ? MaximumDuration : duration;
    }
}
