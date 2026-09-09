using System;
using System.Globalization;
using Counterpoint.Domain.Pricing;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// A requested discount is above its cap and no owner override was supplied to authorise it (SRS
/// FR-1.7, Q-12, task P1-T08 step 2). The same shape as
/// <c>Counterpoint.Application.Catalogue.DuplicateProductWarningException</c>: not a hard block,
/// a caller may obtain an <c>OverrideToken</c> from <c>IOwnerOverrideService</c> for
/// <see cref="Action"/> and resubmit.
/// </summary>
public sealed class DiscountLimitExceededException : InvalidOperationException
{
    public DiscountLimitExceededException(DiscountEvaluation evaluation, string action)
        : base(BuildMessage(evaluation))
    {
        ArgumentNullException.ThrowIfNull(action);

        Evaluation = evaluation;
        Action = action;
    }

    /// <summary>What was asked for, and the cap it was measured against.</summary>
    public DiscountEvaluation Evaluation { get; }

    /// <summary>
    /// The <c>OwnerOverrideRequest.Action</c> (<see cref="PricingAuditActions"/>) an owner must
    /// authorise before this discount can be applied.
    /// </summary>
    public string Action { get; }

    private static string BuildMessage(DiscountEvaluation evaluation) => string.Create(
        CultureInfo.InvariantCulture,
        $"A discount of {evaluation.Rate} is above the {evaluation.Cap} limit. An owner has to authorise this before it can be applied.");
}
