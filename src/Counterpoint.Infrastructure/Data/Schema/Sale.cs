namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>sale</c>. APPEND ONLY apart from <c>status</c>, <c>cancelled_by</c> and
/// <c>cancelled_at</c>; hash chained. See Schema/README.md.
/// </summary>
internal sealed class Sale
{
    public long Id { get; set; }

    public string BillNo { get; set; } = string.Empty;

    public DateTimeOffset SoldAt { get; set; }

    /// <summary>
    /// <c>YYYY-MM-DD</c> TEXT, not a timestamp: it is the grouping key every rollup and
    /// date-range report uses, and running it through the timestamp converter would corrupt it.
    /// </summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>No foreign key yet: <c>customer</c> arrives in P5-T02. See §13 of the data model.</summary>
    public long? CustomerId { get; set; }

    public long UserId { get; set; }

    public long ShiftId { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long Subtotal { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long LineDiscount { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long BillDiscount { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long Tax { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long Rounding { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long Total { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long Cogs { get; set; }

    public string Status { get; set; } = string.Empty;

    public long? CancelledBy { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public string? Note { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string RowHash { get; set; } = string.Empty;
}
