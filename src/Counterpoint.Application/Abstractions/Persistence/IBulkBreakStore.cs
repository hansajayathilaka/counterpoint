using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Writes <c>bulk_break</c> (docs/01_DATA_MODEL.md §4, SRS FR-4.9, AC-09, task P2-T09).</summary>
/// <remarks>
/// Not append-only (CLAUDE.md invariant 5 names exactly which tables are, and <c>bulk_break</c> is
/// not among them - see <c>Counterpoint.Infrastructure.Data.Schema.BulkBreak</c>'s own remarks).
/// The one write here mints the id the paired <c>BULK_BREAK_OUT</c>/<c>BULK_BREAK_IN</c>/wastage
/// <c>DAMAGE</c> <see cref="IStockLedger"/> postings all share as <c>ref_doc_id</c> - it must be
/// inserted, and read back, before any of the three movements post.
/// </remarks>
public interface IBulkBreakStore
{
    /// <summary>
    /// Inserts the header row, in the caller's transaction, and returns its id.
    /// </summary>
    public Task<long> CreateAsync(NewBulkBreak bulkBreak, CancellationToken cancellationToken = default);
}
