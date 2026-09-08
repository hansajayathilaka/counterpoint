using System;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Labels;

/// <summary>One row of the label screen's product search (SRS FR-2.10, FR-2.11).</summary>
public sealed class LabelSearchResultViewModel
{
    public LabelSearchResultViewModel(ProductSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ProductVariantId = result.ProductVariantId;
        ProductName = result.ProductName;
        Sku = result.Sku;
        UomSymbol = result.UomSymbol;
        UnitPriceText = SettingsText.FromMoney(result.UnitPrice);
    }

    public long ProductVariantId { get; }

    public string ProductName { get; }

    public string Sku { get; }

    public string UomSymbol { get; }

    public string UnitPriceText { get; }
}
