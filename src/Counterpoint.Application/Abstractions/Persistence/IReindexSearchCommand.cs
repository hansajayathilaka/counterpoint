using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Rebuilds <c>product_search</c> from the catalogue as it stands now (SRS FR-2.11, NFR-P2,
/// docs/01_DATA_MODEL.md §3 "the contentless-delete limitation, in full").
/// </summary>
/// <remarks>
/// The remedy for the one documented way the index can go stale: a contentless FTS5 table's
/// maintenance triggers look brand and category names up again when a row is removed, so renaming
/// a brand or a category between one edit of a product and the next leaves the old name as a term
/// nothing will ever search for again. The cost is a wrong search result, never a wrong price or a
/// wrong bill, and this command is how the shop clears it - a full delete-all followed by the same
/// backfill the schema migration seeds a freshly upgraded till with.
/// </remarks>
public interface IReindexSearchCommand
{
    /// <summary>Clears and rebuilds the index. Returns how many variants it now covers.</summary>
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default);
}
