using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of a product's unit-of-measure grid (SRS FR-2.4, FR-2.5).</summary>
public sealed class ProductUomOptionRowViewModel
{
    public ProductUomOptionRowViewModel(ProductUomRecord option)
    {
        ArgumentNullException.ThrowIfNull(option);

        Id = option.Id;
        UomId = option.UomId;
        UomSymbol = option.UomSymbol;
        FactorText = option.Conversion.Factor.ToString(CultureInfo.InvariantCulture);
        SellingPriceText = option.SellingPrice is { } price
            ? price.Amount.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        IsBase = option.IsBase;
        StateText = option.IsBase ? "base unit" : "";
    }

    public long Id { get; }

    public long UomId { get; }

    public string UomSymbol { get; }

    public string FactorText { get; }

    public string SellingPriceText { get; }

    public bool IsBase { get; }

    public string StateText { get; }
}
