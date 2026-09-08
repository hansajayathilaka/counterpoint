using System;
using System.Collections.Generic;
using System.Globalization;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// Applying a bulk price update would set one or more prices at or below their product's cost
/// (SRS FR-2.18). Not a hard block - the caller shows the shop <see cref="Lines"/> and resubmits
/// the same <see cref="BulkPriceUpdateRequest"/> with <see cref="BulkPriceUpdateRequest.ConfirmBelowCost"/> set.
/// </summary>
public sealed class BulkPriceBelowCostWarningException : InvalidOperationException
{
    public BulkPriceBelowCostWarningException(IReadOnlyList<BulkPriceUpdatePreviewLine> lines)
        : base(BuildMessage(lines))
    {
        ArgumentNullException.ThrowIfNull(lines);
        Lines = lines;
    }

    /// <summary>The lines - already computed by the preview - that would go at or below cost.</summary>
    public IReadOnlyList<BulkPriceUpdatePreviewLine> Lines { get; }

    private static string BuildMessage(IReadOnlyList<BulkPriceUpdatePreviewLine> lines) => string.Create(
        CultureInfo.InvariantCulture,
        $"This update would put {lines.Count} item(s) at or below their cost. Apply again to set them anyway.");
}
