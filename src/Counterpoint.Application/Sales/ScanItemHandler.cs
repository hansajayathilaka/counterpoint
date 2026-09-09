using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Catalogue;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Reads a variant out of the catalogue and projects away everything a cashier may not see.
/// </summary>
public sealed class ScanItemHandler : IScanItem
{
    private readonly IProductLookup _catalogue;

    public ScanItemHandler(IProductLookup catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        _catalogue = catalogue;
    }

    /// <inheritdoc />
    public async Task<ScannedItem?> ScanAsync(string barcode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        var item = await _catalogue.FindByBarcodeAsync(barcode.Trim(), cancellationToken)
            .ConfigureAwait(false);

        // The projection that makes AC-17 structural: CatalogueItem.UnitCost stops here.
        return item is null ? null : ToScannedItem(item);
    }

    /// <inheritdoc />
    public async Task<ScannedItem?> ScanByVariantIdAsync(long productVariantId, CancellationToken cancellationToken = default)
    {
        var item = await _catalogue.FindByVariantIdAsync(productVariantId, cancellationToken).ConfigureAwait(false);

        return item is null ? null : ToScannedItem(item);
    }

    /// <summary>
    /// Builds the cashier-visible view of a catalogue item, pricing every one of its units up
    /// front (SRS FR-2.5, FR-3.7) so a unit switch on the sales screen never costs a second scan.
    /// </summary>
    internal static ScannedItem ToScannedItem(CatalogueItem item)
    {
        var product = item.ToProduct();

        var unitOptions = item.UomOptions
            .Select(option => new ScannedItemUnitOption(
                option.UomId,
                option.Symbol,
                UomConverter.ResolvePrice(item.UnitPrice, option.UomId, product),
                option.IsBase))
            .ToArray();

        return new ScannedItem(
            item.ProductVariantId,
            item.Description,
            item.BaseUomId,
            item.UomSymbol,
            item.UnitPrice,
            item.QtyOnHand,
            item.MaxDiscountRate,
            unitOptions);
    }
}
