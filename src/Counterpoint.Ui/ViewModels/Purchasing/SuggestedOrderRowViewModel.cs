using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>One row of the suggested-order report (SRS FR-4.6).</summary>
public sealed class SuggestedOrderRowViewModel
{
    public SuggestedOrderRowViewModel(SuggestedOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        ProductId = line.ProductId;
        ProductCode = line.ProductCode;
        ProductDescription = line.ProductDescription;
        OnHandText = line.QtyOnHandBase.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.BaseUomSymbol;
        ReorderLevelText = line.ReorderLevel.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.BaseUomSymbol;
        SuggestedQtyText = line.SuggestedQty.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.BaseUomSymbol;
    }

    public long ProductId { get; }

    public string ProductCode { get; }

    public string ProductDescription { get; }

    public string OnHandText { get; }

    public string ReorderLevelText { get; }

    public string SuggestedQtyText { get; }
}
