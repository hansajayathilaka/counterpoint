using System;
using System.Globalization;
using Counterpoint.Application.Sales;

namespace Counterpoint.Ui.ViewModels;

/// <summary>One row of the F6 held-bill list (SRS FR-3.32).</summary>
public sealed class HeldBillRowViewModel
{
    public HeldBillRowViewModel(HeldBillSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Id = summary.Id;
        Label = summary.Label;
        CreatedAtText = summary.CreatedAt.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
        LineCountText = summary.LineCount == 1 ? "1 line" : summary.LineCount.ToString(CultureInfo.InvariantCulture) + " lines";
    }

    public long Id { get; }

    public string Label { get; }

    public string CreatedAtText { get; }

    public string LineCountText { get; }
}
