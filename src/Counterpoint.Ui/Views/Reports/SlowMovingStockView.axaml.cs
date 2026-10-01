using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T06: the slow-moving and dead stock screen (SRS RPT-12, owner-only). Markup and nothing else - every figure is
/// bound to the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class SlowMovingStockView : UserControl
{
    public SlowMovingStockView()
    {
        InitializeComponent();
    }
}
