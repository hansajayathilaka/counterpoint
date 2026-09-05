using Counterpoint.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Stores a <see cref="TaxRate"/> as the <c>INTEGER</c> scaled ×10 000 over the fraction that
/// docs/01_DATA_MODEL.md §1 requires: <c>1500</c> is 15%.
/// </summary>
/// <remarks>
/// Used by <c>tax_class.rate</c> and by the <c>tax_rate</c> snapshot on <c>sale_line</c>. Scaling
/// over the <em>fraction</em> and not over the percent is the schema's convention; keeping it in
/// the value object is what stops the hundred-fold pricing bug appearing on one column and not
/// another.
/// </remarks>
public sealed class ScaledTaxRateConverter : ValueConverter<TaxRate, long>
{
    public ScaledTaxRateConverter()
        : base(
            rate => rate.ToScaled(),
            scaled => TaxRate.FromScaled(scaled))
    {
    }

    /// <summary>Shared instance; the converter holds no state.</summary>
    public static ScaledTaxRateConverter Instance { get; } = new();
}
