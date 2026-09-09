using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Import;

/// <summary>
/// Reads the whole catalogue in the shape <see cref="CatalogueExportRow"/> needs, for
/// <c>CatalogueImportService.ExportCatalogueAsync</c> (SRS FR-2.23).
/// </summary>
/// <remarks>
/// A bulk read, the same shape as <c>IPriceQuery.FindVariantsAsync</c> (P1-T08): one query over
/// every active product and its defining variant, not one round trip per row - an export of a few
/// thousand SKUs has to stay a single read, not a query storm.
/// </remarks>
public interface ICatalogueExportQuery
{
    /// <summary>Every active product, with its defining variant's price, current stock and primary barcode.</summary>
    public Task<IReadOnlyList<CatalogueExportRow>> ListAllAsync(CancellationToken cancellationToken = default);
}
