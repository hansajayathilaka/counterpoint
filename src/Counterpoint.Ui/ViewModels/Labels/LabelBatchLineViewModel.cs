using System;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Labels;

/// <summary>
/// One product in the batch waiting to be printed, and how many labels it gets (SRS FR-2.10,
/// FR-2.12 "quantity-per-label").
/// </summary>
public sealed class LabelBatchLineViewModel : NumericInputViewModel
{
    private string _quantityText;

    public LabelBatchLineViewModel(
        long productVariantId,
        string productName,
        string sku,
        string uomSymbol,
        string unitPriceText,
        int defaultQuantity)
    {
        ProductVariantId = productVariantId;
        ProductName = productName;
        Sku = sku;
        UomSymbol = uomSymbol;
        UnitPriceText = unitPriceText;
        _quantityText = SettingsText.FromInt(defaultQuantity);
    }

    public long ProductVariantId { get; }

    public string ProductName { get; }

    public string Sku { get; }

    public string UomSymbol { get; }

    public string UnitPriceText { get; }

    /// <summary>What the owner typed. Kept as text so a box mid-edit is never rewritten under them.</summary>
    public string QuantityText
    {
        get => _quantityText;
        set => SetNumeric(ref _quantityText, value);
    }

    /// <summary>The quantity as <c>LabelPrintService</c> needs it - at least 1.</summary>
    public int Quantity => Math.Max(SettingsText.ToInt(QuantityText, fallback: 1), 1);
}
