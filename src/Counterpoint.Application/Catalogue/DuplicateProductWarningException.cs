using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// A product with a very similar name and brand already exists (SRS FR-2.24). Not a hard block -
/// unlike a duplicate barcode, a similar name is only ever a probable duplicate, so the caller may
/// resubmit the same command with <see cref="SaveProductCommand.ConfirmDuplicate"/> set to proceed
/// anyway.
/// </summary>
public sealed class DuplicateProductWarningException : InvalidOperationException
{
    public DuplicateProductWarningException(IReadOnlyList<SimilarProductMatch> matches)
        : base(BuildMessage(matches))
    {
        ArgumentNullException.ThrowIfNull(matches);
        Matches = matches;
    }

    /// <summary>The existing products this one looks like it might duplicate, closest first.</summary>
    public IReadOnlyList<SimilarProductMatch> Matches { get; }

    private static string BuildMessage(IReadOnlyList<SimilarProductMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var names = string.Join(", ", matches.Select(match => string.Create(
            CultureInfo.InvariantCulture,
            $"'{match.ProductName}'{(match.BrandName is null ? string.Empty : $" ({match.BrandName})")}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"This looks like a product the shop already has: {names}. Save again to create it anyway, or open the existing product instead.");
    }
}
