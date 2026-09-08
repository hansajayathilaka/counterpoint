using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// Maintaining <c>barcode</c>: multiple codes per variant, one primary, and the internal-barcode
/// generator for loose or unbarcoded items (docs/01_DATA_MODEL.md §3, SRS FR-2.9, FR-2.10, FR-2.24).
/// Owner only, the same as every other catalogue-administration screen (SRS §3.3 ROLE-2, NFR-S2, AC-17).
/// </summary>
[RequiresRole(Role.Owner)]
public interface IBarcodeMaintenance
{
    public Task<IReadOnlyList<BarcodeRecord>> ListForVariantAsync(long variantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a barcode a manufacturer already printed on the item (FR-2.9). The first barcode
    /// a variant gets is always primary, regardless of <paramref name="makePrimary"/>.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The variant does not exist, the barcode is blank, or the barcode is already attached to a
    /// SKU somewhere in the catalogue - FR-2.24's hard block. There is no override for this one.
    /// </exception>
    public Task<long> AddAsync(long variantId, string barcode, bool makePrimary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and attaches a fresh internal barcode for a loose or otherwise unbarcoded item
    /// (FR-2.10): the shop's configured prefix, a gapless serial from
    /// <see cref="Abstractions.Persistence.IBarcodeSerialAllocator"/>, and a check digit. Can
    /// never collide with an existing barcode - FR-2.24's hard block does not need to fire
    /// against a code this call minted itself.
    /// </summary>
    /// <returns>The barcode generated, in case the label needs it before printing (P1-T12).</returns>
    /// <exception cref="System.InvalidOperationException">The variant does not exist.</exception>
    public Task<string> GenerateInternalAsync(long variantId, bool makePrimary, CancellationToken cancellationToken = default);

    /// <summary>Makes this barcode the variant's primary one, clearing the flag on any other.</summary>
    /// <exception cref="System.InvalidOperationException">The barcode does not exist.</exception>
    public Task SetPrimaryAsync(long barcodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a barcode. If it was the variant's primary and others remain, the earliest of what
    /// is left becomes primary in the same transaction - a variant with any barcodes at all always
    /// has exactly one marked primary.
    /// </summary>
    public Task RemoveAsync(long barcodeId, CancellationToken cancellationToken = default);

    /// <summary>The prefix new internal barcodes are generated with (default "20").</summary>
    public Task<string> GetInternalBarcodePrefixAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.ArgumentException">The prefix is not 1-8 digits.</exception>
    public Task SetInternalBarcodePrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
