using System;
using System.Threading;

namespace Counterpoint.Application.Security;

/// <summary>
/// Proof that an owner authorised one action, once (SRS FR-1.7, FR-1.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Single use, and short lived.</b> The command that asked for the override consumes the token
/// as it applies it; a second call gets nothing. That is what stops one "yes" from the owner
/// authorising the second, third and fourth discount after they have walked away, and it is why
/// <see cref="TryConsume"/> is the only way to spend one - reading
/// <see cref="Action"/> proves nothing on its own.
/// </para>
/// <para>
/// The window is <see cref="Validity"/>: long enough for the owner to finish typing and the
/// cashier to press the key, short enough that a token left on a screen is worthless by the time
/// anyone else reaches it.
/// </para>
/// <para>
/// It carries both user ids because the audit row does (SRS FR-1.6): the cashier who asked, and
/// the owner who allowed it.
/// </para>
/// </remarks>
public sealed class OverrideToken
{
    /// <summary>How long a granted override stays spendable.</summary>
    public static TimeSpan Validity => TimeSpan.FromMinutes(2);

    private int _consumed;

    internal OverrideToken(
        string action,
        long requestedByUserId,
        long grantedByUserId,
        DateTimeOffset grantedAt)
    {
        Action = action;
        RequestedByUserId = requestedByUserId;
        GrantedByUserId = grantedByUserId;
        GrantedAt = grantedAt;
        ExpiresAt = grantedAt + Validity;
    }

    /// <summary>The action this token authorises, and only this one.</summary>
    public string Action { get; }

    /// <summary>The cashier who asked (SRS FR-1.7 - they stay signed in throughout).</summary>
    public long RequestedByUserId { get; }

    /// <summary>The owner who allowed it.</summary>
    public long GrantedByUserId { get; }

    /// <summary>When the owner's password verified.</summary>
    public DateTimeOffset GrantedAt { get; }

    /// <summary>When it stops being spendable.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>True once it has been spent.</summary>
    public bool IsConsumed => Volatile.Read(ref _consumed) != 0;

    /// <summary>
    /// Spends the token for <paramref name="action"/>. Returns true exactly once, and only while
    /// the token is still valid and the action matches the one that was authorised.
    /// </summary>
    /// <remarks>
    /// The action is checked before the token is marked spent, so asking for the wrong action
    /// does not quietly burn an override the cashier is still entitled to use.
    /// </remarks>
    public bool TryConsume(string action, DateTimeOffset now)
    {
        if (!string.Equals(action, Action, StringComparison.Ordinal) || now > ExpiresAt)
        {
            return false;
        }

        return Interlocked.Exchange(ref _consumed, 1) == 0;
    }
}
