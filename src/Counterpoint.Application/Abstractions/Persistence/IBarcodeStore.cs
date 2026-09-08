using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>barcode</c> - multiple codes per variant, one of them primary
/// (docs/01_DATA_MODEL.md §3, SRS FR-2.9, FR-2.10, FR-2.24).
/// </summary>
/// <remarks>
/// A write port, not the hot scan path: <c>barcode.barcode</c> is UNIQUE and that index is what
/// <see cref="IProductLookup.FindByBarcodeAsync"/> reads at the till (NFR-P1). This interface is
/// for maintaining the table - adding, generating, reassigning the primary flag, removing - which
/// happens rarely and off the sale path, so it goes through the unit of work like every other
/// catalogue store rather than a prepared read statement.
/// </remarks>
public interface IBarcodeStore
{
    public Task<IReadOnlyList<BarcodeRecord>> ListForVariantAsync(long variantId, CancellationToken cancellationToken = default);

    public Task<BarcodeRecord?> FindByIdAsync(long barcodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The existing owner of <paramref name="barcode"/>, or null when it is not yet used anywhere
    /// in the catalogue - the pre-check behind FR-2.24's hard block.
    /// </summary>
    public Task<BarcodeConflict?> FindConflictAsync(string barcode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts one barcode row, in the caller's transaction.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The barcode is already used - the database's own unique index refused the write, the
    /// backstop behind <see cref="FindConflictAsync"/>'s pre-check (FR-2.24).
    /// </exception>
    public Task<long> AddAsync(long variantId, string barcode, bool isPrimary, CancellationToken cancellationToken = default);

    /// <summary>Clears <c>is_primary</c> on every barcode of <paramref name="variantId"/>.</summary>
    public Task ClearPrimaryAsync(long variantId, CancellationToken cancellationToken = default);

    /// <summary>Sets <c>is_primary</c> on exactly this row. The caller clears any other first.</summary>
    public Task SetPrimaryAsync(long barcodeId, CancellationToken cancellationToken = default);

    public Task RemoveAsync(long barcodeId, CancellationToken cancellationToken = default);
}
