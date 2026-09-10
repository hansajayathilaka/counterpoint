using System;
using Counterpoint.Domain.Returns;

namespace Counterpoint.Application.Returns;

/// <summary>
/// A return is against policy and either no override can ever apply (AC-06) or none was supplied
/// for this attempt (SRS FR-5.6, FR-5.10, FR-5.13, FR-5.19, task P2-T01).
/// </summary>
/// <remarks>
/// The same shape as <c>Counterpoint.Application.Pricing.DiscountLimitExceededException</c>: not
/// necessarily a hard stop - when <see cref="Eligibility"/> is a
/// <see cref="ReturnEligibility.AllowedWithOverride"/>, a caller may obtain an
/// <c>OverrideToken</c> from <c>IOwnerOverrideService</c> for <see cref="Action"/> and resubmit.
/// When <see cref="Eligibility"/> is a <see cref="ReturnEligibility.Denied"/>, <see cref="Action"/>
/// is null - there is nothing to ask an owner for, by design (AC-06's cumulative-quantity rule
/// never produces one at all, and the disabled-unlinked-returns rule is a settings change, not a
/// per-transaction override).
/// </remarks>
public sealed class ReturnNotEligibleException : InvalidOperationException
{
    public ReturnNotEligibleException(ReturnEligibility eligibility, string? action)
        : base(BuildMessage(eligibility))
    {
        ArgumentNullException.ThrowIfNull(eligibility);

        Eligibility = eligibility;
        Action = action;
    }

    /// <summary>What was evaluated, and why it did not simply pass.</summary>
    public ReturnEligibility Eligibility { get; }

    /// <summary>
    /// The <c>OwnerOverrideRequest.Action</c> (<see cref="ReturnPolicyAuditActions"/>) an owner
    /// has to authorise before this return can proceed, or null when no override exists for this
    /// rule at all.
    /// </summary>
    public string? Action { get; }

    private static string BuildMessage(ReturnEligibility eligibility) => eligibility switch
    {
        ReturnEligibility.Denied denied => denied.Reason,
        ReturnEligibility.AllowedWithOverride allowedWithOverride =>
            allowedWithOverride.Reason + " No owner override was supplied.",
        _ => "This return is not eligible.",
    };
}
