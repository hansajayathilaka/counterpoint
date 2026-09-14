namespace Counterpoint.Domain.Inventory;

/// <summary>
/// The two manual, no-document stock-change movements the owner's adjustment door posts
/// (SRS FR-4, task P2-T08). Distinct from <c>Counterpoint.Domain.Returns.ReturnDisposition</c>:
/// this is not what happens to a returned unit, it is a correction that has no originating
/// document (a GRN, a sale, a return) to carry its own quantity at all.
/// </summary>
public enum AdjustmentType
{
    /// <summary>
    /// A stock count correction - up or down (docs/01_DATA_MODEL.md §4's <c>ADJUSTMENT</c>).
    /// </summary>
    Adjustment = 0,

    /// <summary>
    /// A write-off of damaged stock - always down (docs/01_DATA_MODEL.md §4's <c>DAMAGE</c>).
    /// </summary>
    Damage = 1,
}
