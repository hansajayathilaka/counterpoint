namespace Counterpoint.Domain.Security;

/// <summary>
/// The two roles on the one machine (SRS §3.3 ROLE-1 and ROLE-2, FR-1.2).
/// </summary>
/// <remarks>
/// <para>
/// It lives in <c>Counterpoint.Domain</c> because it is the shop's vocabulary and nothing else -
/// a pure enum with no framework behind it. Both <c>Counterpoint.Application</c> (which enforces
/// it) and <c>Counterpoint.Ui</c> (which may hide a button with it) can therefore see it without
/// either of them reaching for the other.
/// </para>
/// <para>
/// <see cref="Cashier"/> is deliberately the zero value: a role field that was never set defaults
/// to the <em>lower</em> privilege, so forgetting to assign one cannot grant anything.
/// </para>
/// <para>
/// There is no third role and there must not be one. The permission split is a consequence of
/// C-01 (one machine, one till, one active session), not a configuration point.
/// </para>
/// </remarks>
public enum Role
{
    /// <summary>The person at the counter. Sell, return within policy, reprint, check stock.</summary>
    Cashier = 0,

    /// <summary>
    /// Proprietor or manager. Everything a cashier can do, plus cost prices, purchasing, stock
    /// adjustments, price changes, overrides, all reports, settings, backup and user management.
    /// </summary>
    Owner = 1,
}
