using System;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// A shift close's cash variance is above <c>policy.shift_close_variance_note_threshold</c> and no
/// note was supplied to explain it (SRS FR-8.4, task P3-T03 "Do this" #1). The same shape as
/// <see cref="Counterpoint.Application.Cash.CashOutAuthorisationRequiredException"/>: not a hard
/// block - the caller supplies a note and resubmits.
/// </summary>
public sealed class ShiftCloseVarianceNoteRequiredException : InvalidOperationException
{
    public ShiftCloseVarianceNoteRequiredException(Money variance, Money threshold)
        : base(BuildMessage(variance, threshold))
    {
        Variance = variance;
        Threshold = threshold;
    }

    /// <summary>The variance that triggered the requirement, over (positive) or short (negative).</summary>
    public Money Variance { get; }

    /// <summary><c>policy.shift_close_variance_note_threshold</c> at the time of the attempt.</summary>
    public Money Threshold { get; }

    private static string BuildMessage(Money variance, Money threshold) => string.Create(
        CultureInfo.InvariantCulture,
        $"A cash variance of {variance} is above the {threshold} limit. A note explaining it is required before this shift can close.");
}
