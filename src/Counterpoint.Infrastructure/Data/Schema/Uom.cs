namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>uom</c>. See Schema/README.md.</summary>
internal sealed class Uom
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    /// <summary>Plain count, 0-4. Not scaled.</summary>
    public int DecimalPlaces { get; set; }
}
