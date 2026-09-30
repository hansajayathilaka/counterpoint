using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T05: RPT-02, the sales-by-item screen. Markup and nothing else - every figure is bound to
/// the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class SalesByItemReportView : UserControl
{
    public SalesByItemReportView()
    {
        InitializeComponent();
    }
}
