namespace Counterpoint.Application.Reporting;

/// <summary>
/// Where a report query is allowed to read its figures from (task P3-T04 "Do this" #3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Rollups cannot serve every report, so this is a per-query choice, not a blanket rule.</b>
/// <c>daily_sales_summary</c> keeps no hour-of-day dimension (RPT-01's by-hour breakdown cannot be
/// answered from it), rolls every tender but CASH and CARD into a single <c>tender_other</c>
/// bucket, and its per-product companion <c>daily_product_summary.net</c> does not subtract the
/// bill-level discount. A report that needs any of those must ask for
/// <see cref="RawTablesRequired"/> and read the raw tables; a report that needs only the canonical
/// period figures can let the query read rollups for closed dates and raw tables for the open
/// shift's own date.
/// </para>
/// <para>
/// <b>This is an optimisation switch, never a correctness switch.</b> Both settings must return
/// the same figures for the same range - that equality is task P3-T04's own "Done when" and is
/// proven by <c>Counterpoint.Integration.Tests.Reporting.ReportQueryLayerTests</c>. A report picks
/// <see cref="RawTablesRequired"/> only when it needs a dimension the rollup does not carry, never
/// because rollups disagree with raw data.
/// </para>
/// </remarks>
public enum ReportSourcePolicy
{
    /// <summary>
    /// Read rollups for business dates that are closed and carry one, and raw tables for the open
    /// shift's own business date and for any closed date whose rollup row is absent (the default).
    /// </summary>
    RollupsWhereClosed = 0,

    /// <summary>
    /// Read the raw tables for the whole range, ignoring the rollups entirely: <c>sale</c> (whose
    /// <c>cogs</c> header is the cost figure, so <c>sale_line</c> is never read), <c>sale_return</c>,
    /// <c>sale_return_line</c> and <c>payment</c>. The opt-in a report with a dimension the rollup
    /// does not carry must ask for.
    /// </summary>
    RawTablesRequired = 1,
}
