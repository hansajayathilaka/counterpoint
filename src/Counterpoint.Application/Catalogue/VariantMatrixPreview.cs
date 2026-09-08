using System.Collections.Generic;
using Counterpoint.Domain.Catalogue;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// What generating a variant matrix would create, before anything is written (SRS FR-2.6).
/// </summary>
/// <param name="ToCreate">Every combination that does not already have a variant.</param>
/// <param name="SkippedExistingCount">How many combinations were left out because a variant already exists for them.</param>
public sealed record VariantMatrixPreview(IReadOnlyList<VariantAttributes> ToCreate, int SkippedExistingCount)
{
    /// <summary>The full size of the cartesian product the axes describe.</summary>
    public int TotalCombinations => ToCreate.Count + SkippedExistingCount;
}
