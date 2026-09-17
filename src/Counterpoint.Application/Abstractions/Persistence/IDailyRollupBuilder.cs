using System;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Rebuilds <c>daily_sales_summary</c> and <c>daily_product_summary</c> for one business date,
/// from the raw <c>sale</c>, <c>sale_line</c>, <c>sale_return</c> and <c>sale_return_line</c>
/// tables - the rollup half of a Z report close (task P3-T03 "Do this" #2, SRS FR-8.4, NFR-P5).
/// </summary>
/// <remarks>
/// <para>
/// <b>A full rebuild of the date, not an incremental append.</b> Both rollup tables are keyed on
/// <c>business_date</c> alone (<c>daily_product_summary</c> adds <c>product_variant_id</c>), not on
/// the closing shift, because more than one shift can trade on the same calendar day (a crash and
/// reopen, say). <see cref="RebuildAsync"/> therefore recomputes the whole day from scratch and
/// replaces whatever was there - the same "rebuildable projection, never trusted incrementally"
/// discipline CLAUDE.md invariant 3 already asks of the stock quantity projection - so a
/// correction posted earlier the same day, or a second shift closing later the same day, always
/// leaves the row an exact recomputation of everything on that date, never a stale partial figure
/// from the first shift alone.
/// </para>
/// <para>
/// Called inside the same transaction as the shift-close write, the audit row and the
/// <c>print_job</c> enqueue (task P3-T03 "Do this" #2: "all in one").
/// </para>
/// </remarks>
public interface IDailyRollupBuilder
{
    /// <summary>Recomputes and replaces both rollup tables' rows for one business date.</summary>
    /// <param name="businessDate">The date to rebuild.</param>
    /// <param name="builtAt">Stamped onto <c>daily_sales_summary.built_at</c>.</param>
    /// <param name="cancellationToken">Cancels the rebuild.</param>
    public Task RebuildAsync(
        DateOnly businessDate,
        DateTimeOffset builtAt,
        CancellationToken cancellationToken = default);
}
