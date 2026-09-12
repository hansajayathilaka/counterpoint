using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>One line of a purchase order being viewed (SRS FR-4.5, FR-4.10).</summary>
public sealed class PurchaseOrderLineRowViewModel
{
    public PurchaseOrderLineRowViewModel(PurchaseOrderLineRecord line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Sku = line.Sku;
        ProductDescription = line.ProductDescription;
        QtyText = line.Qty.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.UomSymbol;
        UnitCostText = line.UnitCost.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        LineTotalText = line.LineTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        ReceivedText = line.QtyReceivedBase.Value.ToString("0.####", CultureInfo.InvariantCulture)
            + " " + line.BaseUomSymbol + " received";
    }

    public string Sku { get; }

    public string ProductDescription { get; }

    public string QtyText { get; }

    public string UnitCostText { get; }

    public string LineTotalText { get; }

    public string ReceivedText { get; }
}
