namespace Counterpoint.Domain.Purchasing;

/// <summary>
/// A purchase order's lifecycle (docs/01_DATA_MODEL.md §4, <c>ck_purchase_order_status</c>, SRS
/// FR-4.5, FR-4.10).
/// </summary>
/// <remarks>
/// <see cref="Draft"/> is deliberately the zero value, the same reasoning as
/// <c>Counterpoint.Domain.Catalogue.ProductType.Standard</c>: a purchase order nobody has sent
/// yet is the state that costs nothing if a caller forgets to set one.
/// </remarks>
public enum PurchaseOrderStatus
{
    /// <summary>Being built. Not yet sent to the supplier - freely editable, freely cancellable.</summary>
    Draft = 0,

    /// <summary>Sent to the supplier. Nothing has been received against it yet.</summary>
    Sent = 1,

    /// <summary>Some, but not all, lines are fully received.</summary>
    Partial = 2,

    /// <summary>Every line is fully received. Terminal - nothing more can land against it.</summary>
    Received = 3,

    /// <summary>Cancelled before completion. Terminal - keeps its number (CLAUDE.md invariant 4).</summary>
    Cancelled = 4,
}
