using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>One row of the purchase-order line product search (SRS FR-2.11, FR-4.5).</summary>
public sealed class PurchaseOrderSearchResultViewModel
{
    public PurchaseOrderSearchResultViewModel(ProductSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ProductVariantId = result.ProductVariantId;
        Sku = result.Sku;
        ProductName = result.ProductName;
        StockText = result.QtyOnHand.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + result.UomSymbol + " on hand";
    }

    public long ProductVariantId { get; }

    public string Sku { get; }

    public string ProductName { get; }

    public string StockText { get; }
}
