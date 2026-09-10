namespace Counterpoint.Application.Returns;

/// <summary>
/// The <c>audit_log.action</c> and <c>OwnerOverrideRequest.Action</c> tokens this area writes
/// (SRS FR-5.6, FR-5.10, FR-5.13, FR-5.19, task P2-T01).
/// </summary>
/// <remarks>
/// Named constants for the same reason <see cref="Counterpoint.Application.Pricing.PricingAuditActions"/>
/// and <see cref="Counterpoint.Application.Security.SecurityAuditActions"/> are: the audit-log
/// viewer (P3-T08) filters on exactly these strings, and
/// <see cref="Counterpoint.Application.Security.OverrideToken.TryConsume"/> only spends a token
/// for the action it names - a typo here would either hide an event or make an override
/// unspendable. <see cref="UnlinkedReturn"/> is the same literal token
/// <see cref="Counterpoint.Application.Security.OwnerOverrideRequest"/>'s own remarks already use
/// as an example.
/// </remarks>
public static class ReturnPolicyAuditActions
{
    /// <summary>A return outside the return window, asked of the owner (SRS FR-5.6).</summary>
    public const string ReturnWindowExceeded = "RETURN_WINDOW_EXCEEDED";

    /// <summary>A non-returnable product or category, asked of the owner (SRS FR-5.10, AC-05).</summary>
    public const string NonReturnableOverride = "NON_RETURNABLE_OVERRIDE";

    /// <summary>A return with no original bill, always asked of the owner when the feature is enabled (SRS FR-5.19).</summary>
    public const string UnlinkedReturn = "UNLINKED_RETURN";

    /// <summary>A return whose bill was found without its own number being scanned or typed (SRS FR-5.1, Q-03).</summary>
    public const string ReceiptNotPresented = "RETURN_WITHOUT_RECEIPT_OVERRIDE";

    /// <summary>A cash refund above the configured limit, asked of the owner (SRS FR-5.13).</summary>
    public const string CashRefundLimitExceeded = "CASH_REFUND_LIMIT_EXCEEDED";
}
