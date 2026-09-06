using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>cash_movement</c> (docs/01_DATA_MODEL.md §7). APPEND ONLY. See Schema/README.md.
/// </summary>
internal sealed class CashMovement
{
    public long Id { get; set; }

    public long ShiftId { get; set; }

    /// <summary><c>IN</c> or <c>OUT</c>. The amount itself is always positive.</summary>
    public string Direction { get; set; } = string.Empty;

    public Money Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public long UserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
