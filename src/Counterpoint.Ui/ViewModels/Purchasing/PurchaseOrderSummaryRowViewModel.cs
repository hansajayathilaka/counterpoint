using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>One row of the purchase order list (SRS FR-4.5).</summary>
public sealed class PurchaseOrderSummaryRowViewModel
{
    public PurchaseOrderSummaryRowViewModel(PurchaseOrderSummaryRecord order)
    {
        ArgumentNullException.ThrowIfNull(order);

        Id = order.Id;
        PoNo = order.PoNo;
        SupplierName = order.SupplierName;
        OrderedAtText = order.OrderedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ExpectedAtText = order.ExpectedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        Status = order.Status;
        LineCount = order.LineCount;
        TotalText = order.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public long Id { get; }

    public string PoNo { get; }

    public string SupplierName { get; }

    public string OrderedAtText { get; }

    public string ExpectedAtText { get; }

    public string Status { get; }

    public int LineCount { get; }

    public string TotalText { get; }
}
