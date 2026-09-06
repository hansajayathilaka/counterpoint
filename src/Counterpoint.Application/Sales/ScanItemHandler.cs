using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;

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
        return item is null
            ? null
            : new ScannedItem(
                item.ProductVariantId,
                item.Description,
                item.BaseUomId,
                item.UomSymbol,
                item.UnitPrice);
    }
}
