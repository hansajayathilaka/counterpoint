using System;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Cash;

/// <summary>
/// A requested cash-out is above <c>policy.cash_out_authorisation_threshold</c> and no owner
/// override was supplied to authorise it (SRS FR-1.7, task P3-T01 "Do this" #3). The same shape as
/// <see cref="Counterpoint.Application.Pricing.DiscountLimitExceededException"/>: not a hard block,
/// a caller may obtain an <c>OverrideToken</c> from <c>IOwnerOverrideService</c> for
/// <see cref="CashMovementAuditActions.CashOutAboveThreshold"/> and resubmit.
/// </summary>
public sealed class CashOutAuthorisationRequiredException : InvalidOperationException
{
    public CashOutAuthorisationRequiredException(Money amount, Money threshold)
        : base(BuildMessage(amount, threshold))
    {
        Amount = amount;
        Threshold = threshold;
    }

    /// <summary>What was asked to be taken out.</summary>
    public Money Amount { get; }

    /// <summary><c>policy.cash_out_authorisation_threshold</c> at the time of the attempt.</summary>
    public Money Threshold { get; }

    private static string BuildMessage(Money amount, Money threshold) => string.Create(
        CultureInfo.InvariantCulture,
        $"A cash-out of {amount} is above the {threshold} limit. An owner has to authorise this before it can be paid out.");
}
