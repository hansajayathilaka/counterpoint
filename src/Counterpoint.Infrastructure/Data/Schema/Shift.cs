namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>shift</c>. APPEND ONLY apart from the close fields, which are settable once.
/// See Schema/README.md.
/// </summary>
internal sealed class Shift
{
    public long Id { get; set; }

    public string ShiftNo { get; set; } = string.Empty;

    public long UserId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. See <see cref="Sale.BusinessDate"/>.</summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>Money ×10 000.</summary>
    public long OpeningFloat { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long? CountedCash { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long? ExpectedCash { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long? Variance { get; set; }

    public string Status { get; set; } = string.Empty;

    public long? ClosedBy { get; set; }

    public string? Note { get; set; }
}
