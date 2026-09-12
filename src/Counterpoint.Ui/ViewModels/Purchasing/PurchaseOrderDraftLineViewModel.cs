using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>
/// One line being added to a new purchase order, before it is saved (SRS FR-4.5). Quantity and
/// unit cost are kept as text - exactly as <c>SaleLineViewModel</c> keeps a quantity while it is
/// being typed - and parsed only when the draft is saved.
/// </summary>
public sealed partial class PurchaseOrderDraftLineViewModel : ObservableObject
{
    public PurchaseOrderDraftLineViewModel(long productVariantId, string sku, string productDescription, long uomId, string uomSymbol)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(productDescription);
        ArgumentNullException.ThrowIfNull(uomSymbol);

        ProductVariantId = productVariantId;
        Sku = sku;
        ProductDescription = productDescription;
        UomId = uomId;
        UomSymbol = uomSymbol;
    }

    public long ProductVariantId { get; }

    public string Sku { get; }

    public string ProductDescription { get; }

    public long UomId { get; }

    public string UomSymbol { get; }

    [ObservableProperty]
    private string _quantity = "1";

    [ObservableProperty]
    private string _unitCost = "0.00";
}
