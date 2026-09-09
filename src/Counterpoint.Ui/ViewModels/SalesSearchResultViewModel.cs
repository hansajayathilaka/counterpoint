using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels;

/// <summary>One row of the F3 counter-search result list (SRS FR-2.11, FR-3.3).</summary>
public sealed class SalesSearchResultViewModel
{
    public SalesSearchResultViewModel(ProductSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ProductVariantId = result.ProductVariantId;
        Sku = result.Sku;
        ProductName = result.ProductName;
        PriceText = result.UnitPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " / " + result.UomSymbol;
        StockText = result.QtyOnHand.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + result.UomSymbol + " on hand";
        LocationText = result.Location ?? string.Empty;
    }

    public long ProductVariantId { get; }

    public string Sku { get; }

    public string ProductName { get; }

    public string PriceText { get; }

    public string StockText { get; }

    public string LocationText { get; }
}
