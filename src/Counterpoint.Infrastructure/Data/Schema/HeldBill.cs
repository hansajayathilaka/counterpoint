namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>held_bill</c> (docs/01_DATA_MODEL.md §5): a parked bill, held as JSON. Nothing
/// half-finished reaches <c>sale</c>, so this is the only place an in-progress bill exists.
/// See Schema/README.md.
/// </summary>
internal sealed class HeldBill
{
    public long Id { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the in-progress bill.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public long UserId { get; set; }
}
