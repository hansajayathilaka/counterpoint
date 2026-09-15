namespace Counterpoint.Application.Cash;

/// <summary>
/// The <c>audit_log.action</c> and <c>OwnerOverrideRequest.Action</c> tokens this area writes
/// (SRS FR-7.7, FR-8.2, FR-8.6, FR-8.7, task P3-T01).
/// </summary>
/// <remarks>
/// Named constants for the same reason <see cref="Counterpoint.Application.Pricing.PricingAuditActions"/>
/// and <see cref="Counterpoint.Application.Returns.ReturnPolicyAuditActions"/> are: the audit-log
/// viewer and exceptions report (P3-T08) filter on exactly these strings, and
/// <see cref="Counterpoint.Application.Security.OverrideToken.TryConsume"/> only spends a token for
/// the action it names.
/// </remarks>
public static class CashMovementAuditActions
{
    /// <summary>A cash-out above the configured threshold, asked of the owner (task P3-T01 "Do this" #3).</summary>
    public const string CashOutAboveThreshold = "CASH_OUT_ABOVE_THRESHOLD";

    /// <summary>
    /// A drawer opened with no sale behind it, always owner-authorised (SRS FR-7.7, task P3-T01
    /// "Do this" #5). The exact literal <see cref="Counterpoint.Application.Security.OwnerOverrideRequest"/>'s
    /// own remarks already give as an example.
    /// </summary>
    public const string NoSaleDrawerOpen = "NO_SALE_DRAWER";

    /// <summary>The <c>cash_movement</c> table, as <c>audit_log.entity_type</c>.</summary>
    public const string CashMovementEntityType = "cash_movement";

    /// <summary>The <c>shift</c> table, as <c>audit_log.entity_type</c>, for a no-sale drawer open.</summary>
    public const string ShiftEntityType = "shift";
}
