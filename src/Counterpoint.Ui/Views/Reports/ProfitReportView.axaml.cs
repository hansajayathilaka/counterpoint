using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T05: RPT-03, the owner-only profit screen. Markup and nothing else - every figure is bound to
/// the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class ProfitReportView : UserControl
{
    public ProfitReportView()
    {
        InitializeComponent();
    }
}
