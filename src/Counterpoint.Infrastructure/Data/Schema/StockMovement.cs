using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>stock_movement</c>. APPEND ONLY. See Schema/README.md.</summary>
internal sealed class StockMovement
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    public string MovementType { get; set; } = string.Empty;

    /// <summary>Quantity ×10 000, signed: positive increases stock.</summary>
    public long QtyBase { get; set; }

    /// <summary>Cost at the moment of movement.</summary>
    public Money UnitCost { get; set; }

    public string RefDocType { get; set; } = string.Empty;

    public long? RefDocId { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long BalanceAfter { get; set; }

    public long UserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? Note { get; set; }
}
