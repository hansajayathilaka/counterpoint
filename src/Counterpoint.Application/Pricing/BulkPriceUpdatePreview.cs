using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Pricing;

/// <summary>One variant a bulk price update would change, worked out but not written (SRS FR-2.19).</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="ProductName">For display.</param>
/// <param name="Sku">For display.</param>
/// <param name="OldPrice">Today's price.</param>
/// <param name="NewPrice">What the adjustment would set it to.</param>
/// <param name="BelowCost">True when <see cref="NewPrice"/> is at or below the product's cost (SRS FR-2.18).</param>
public sealed record BulkPriceUpdatePreviewLine(
    long ProductVariantId,
    string ProductName,
    string Sku,
    Money OldPrice,
    Money NewPrice,
    bool BelowCost);

/// <summary>
/// The dry run <see cref="IBulkPriceUpdateService.PreviewAsync"/> returns before anything is
/// written (SRS FR-2.19: "with a preview before applying").
/// </summary>
public sealed record BulkPriceUpdatePreview(IReadOnlyList<BulkPriceUpdatePreviewLine> Lines)
{
    /// <summary>How many variants would change.</summary>
    public int Count => Lines.Count;

    /// <summary>True when applying this update would need <see cref="BulkPriceUpdateRequest.ConfirmBelowCost"/> (SRS FR-2.18).</summary>
    public bool AnyBelowCost => Lines.Any(line => line.BelowCost);
}
