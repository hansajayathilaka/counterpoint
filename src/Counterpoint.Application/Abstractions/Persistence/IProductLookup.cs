using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads one sellable variant out of the catalogue (SRS FR-3.2, NFR-P1).
/// </summary>
/// <remarks>
/// A read port, not a repository: it returns a flat read model and never an entity or an
/// <c>IQueryable</c>. The full catalogue search (FTS5, partial names, result lists) is P1-T06;
/// the skeleton needs exactly these two lookups.
/// </remarks>
public interface IProductLookup
{
    /// <summary>
    /// Finds the variant a scanned symbol belongs to, or null when nothing matches.
    /// </summary>
    /// <param name="barcode">The scanned symbol, exactly as the scanner produced it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<CatalogueItem?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a variant by id, or null when it does not exist or is not sellable.
    /// </summary>
    /// <param name="productVariantId">The variant id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<CatalogueItem?> FindByVariantIdAsync(
        long productVariantId,
        CancellationToken cancellationToken = default);
}
