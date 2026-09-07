using System;

namespace Counterpoint.Application.Security;

/// <summary>
/// The answer to a sign-in attempt (SRS FR-1.1, UI-06).
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">
/// What to put on the screen, in plain language with a next step (SRS UI-06). It is composed
/// here rather than in a viewmodel because only this layer knows how long a lock has left to run
/// - and because a screen that had to compose it would be a screen that knew the rule.
/// </param>
/// <param name="User">The user, on success. Null otherwise, always.</param>
/// <param name="LockedUntil">When the lock lifts, when the outcome is a lockout.</param>
/// <remarks>
/// A refusal is a value here, not an exception: typing the wrong password is an ordinary thing
/// that happens at a counter, and the caller has to show a message either way.
/// <see cref="NotAuthorisedException"/> is for the different case where a signed-in user asks for
/// something their role does not cover.
/// </remarks>
public sealed record LoginResult(
    LoginOutcome Outcome,
    string Message,
    AuthenticatedUser? User = null,
    DateTimeOffset? LockedUntil = null)
{
    /// <summary>True only when the password verified and <see cref="User"/> is set.</summary>
    public bool Succeeded => Outcome == LoginOutcome.Succeeded && User is not null;
}
