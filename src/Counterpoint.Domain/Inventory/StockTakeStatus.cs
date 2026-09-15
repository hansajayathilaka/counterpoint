namespace Counterpoint.Domain.Inventory;

/// <summary>The lifecycle of one stock take (P2-T10, SRS FR-4, AC-10).</summary>
public enum StockTakeStatus
{
    /// <summary>Counting is under way. Lines may still be recorded.</summary>
    Open,

    /// <summary>Corrections were posted to <c>stock_movement</c> as one batch. Terminal.</summary>
    Posted,

    /// <summary>Abandoned with nothing posted; stock is untouched. Terminal.</summary>
    Abandoned,
}
