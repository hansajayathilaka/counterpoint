using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T06: the fast-moving items screen (SRS RPT-13, owner-only). Markup and nothing else - every figure is
/// bound to the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class FastMovingView : UserControl
{
    public FastMovingView()
    {
        InitializeComponent();
    }
}
