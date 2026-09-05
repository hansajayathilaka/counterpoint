using Counterpoint.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Stores a <see cref="Percentage"/> as the <c>INTEGER</c> scaled ×10 000 over the fraction that
/// docs/01_DATA_MODEL.md §1 requires: <c>1500</c> is 15.00%.
/// </summary>
/// <remarks>
/// Used by <c>product.max_discount_rate</c>, the only discount-limit column in the schema. Unlike
/// <see cref="ScaledTaxRateConverter"/> this one permits a negative value, because
/// <see cref="Percentage"/> is a general proportion and refusing one here would move a business
/// rule into a converter.
/// </remarks>
public sealed class ScaledPercentageConverter : ValueConverter<Percentage, long>
{
    public ScaledPercentageConverter()
        : base(
            percentage => percentage.ToScaled(),
            scaled => Percentage.FromScaled(scaled))
    {
    }

    /// <summary>Shared instance; the converter holds no state.</summary>
    public static ScaledPercentageConverter Instance { get; } = new();
}
