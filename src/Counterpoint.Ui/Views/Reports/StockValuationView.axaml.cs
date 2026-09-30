using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T06: the stock valuation screen (SRS RPT-09, owner-only). Markup and nothing else - every figure is
/// bound to the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class StockValuationView : UserControl
{
    public StockValuationView()
    {
        InitializeComponent();
    }
}
