namespace Counterpoint.Application.Pricing;

/// <summary>
/// The <c>audit_log.action</c> and <c>OwnerOverrideRequest.Action</c> tokens this area writes
/// (SRS FR-1.6, FR-1.7, FR-2.17, FR-2.19, task P1-T08).
/// </summary>
/// <remarks>
/// Named constants for the same reason <c>SecurityAuditActions</c> and
/// <c>CatalogueAuditActions</c> are: the audit-log viewer (P3-T08) filters on exactly these
/// strings, and <see cref="Counterpoint.Application.Security.OverrideToken.TryConsume"/> only
/// spends a token for the action it names - a typo here would either hide an event or make an
/// override unspendable.
/// </remarks>
public static class PricingAuditActions
{
    /// <summary>A line discount above its cap, asked of the owner (<see cref="IDiscountAuthorisationService.AuthoriseLineDiscount"/>).</summary>
    public const string LineDiscountAboveLimit = "DISCOUNT_ABOVE_LIMIT_LINE";

    /// <summary>A bill discount above its cap, asked of the owner (<see cref="IDiscountAuthorisationService.AuthoriseBillDiscount"/>).</summary>
    public const string BillDiscountAboveLimit = "DISCOUNT_ABOVE_LIMIT_BILL";

    /// <summary>A variant's price was changed (SRS FR-2.17). <c>price_change_log</c>'s own table is the record; this is what names the event on <c>audit_log</c> for anything that also logs there.</summary>
    public const string PriceChanged = "PRICE_CHANGED";

    /// <summary>A bulk price update by category, brand or supplier was applied (SRS FR-2.19).</summary>
    public const string BulkPriceUpdateApplied = "BULK_PRICE_UPDATE_APPLIED";
}
