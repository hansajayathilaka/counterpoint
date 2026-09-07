namespace Counterpoint.Application.Security;

/// <summary>
/// A cashier asking the owner, at the counter, to authorise something the cashier may not do
/// (SRS FR-1.7).
/// </summary>
/// <param name="Action">
/// What is being authorised, as a stable token the audit log can be filtered on -
/// <c>UNLINKED_RETURN</c>, <c>DISCOUNT_ABOVE_LIMIT</c>, <c>NO_SALE_DRAWER</c> and so on. The
/// commands that use these are P2-T03, P1-T08 and P3-T01; this task defines the mechanism, not
/// its consumers.
/// </param>
/// <param name="Reason">
/// Why, in the owner's own words. Required: an override with no reason is an audit row that
/// tells a future reader nothing.
/// </param>
/// <param name="OwnerUsername">The owner standing at the till.</param>
/// <param name="OwnerPassword">
/// Their password, held only long enough to verify. "Re-authenticates an owner" is what makes
/// this an authorisation rather than a button, so the credential has to be part of the ask.
/// </param>
public sealed record OwnerOverrideRequest(
    string Action,
    string Reason,
    string OwnerUsername,
    string OwnerPassword);
