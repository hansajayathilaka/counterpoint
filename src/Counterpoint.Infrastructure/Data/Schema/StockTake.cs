namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>stock_take</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.</summary>
internal sealed class StockTake
{
    public long Id { get; set; }

    /// <summary><c>ALL</c>, <c>CATEGORY:12</c>, <c>LOCATION:A3</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public long UserId { get; set; }
}
