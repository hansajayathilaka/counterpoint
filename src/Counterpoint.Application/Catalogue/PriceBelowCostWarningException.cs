using System;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// A variant's price was set at or below the product's cost (SRS FR-2.18). Not a hard block, the
/// same shape as <see cref="DuplicateProductWarningException"/>: the caller shows the shop what
/// was found and resubmits the same <see cref="SaveProductVariantCommand"/> with
/// <see cref="SaveProductVariantCommand.ConfirmBelowCost"/> set to save it anyway.
/// </summary>
public sealed class PriceBelowCostWarningException : InvalidOperationException
{
    public PriceBelowCostWarningException(Money price, Money cost)
        : base(BuildMessage(price, cost))
    {
        Price = price;
        Cost = cost;
    }

    /// <summary>The price that was about to be saved.</summary>
    public Money Price { get; }

    /// <summary>The product's moving-average cost it was compared against.</summary>
    public Money Cost { get; }

    private static string BuildMessage(Money price, Money cost) => string.Create(
        CultureInfo.InvariantCulture,
        $"A price of {price} is at or below this product's cost of {cost}. Every sale at this price would lose money. Save again to set it anyway.");
}
