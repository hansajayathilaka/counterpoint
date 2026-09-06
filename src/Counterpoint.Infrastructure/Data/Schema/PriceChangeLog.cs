using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>price_change_log</c> (docs/01_DATA_MODEL.md §3, FR-2.17). See Schema/README.md.
/// </summary>
internal sealed class PriceChangeLog
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    public Money OldPrice { get; set; }

    public Money NewPrice { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public long UserId { get; set; }

    public string? Reason { get; set; }
}
