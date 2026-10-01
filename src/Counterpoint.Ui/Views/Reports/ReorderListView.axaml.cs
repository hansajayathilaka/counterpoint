using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T06: the low stock and reorder screen (SRS RPT-10, both roles). Markup and nothing else - every figure is
/// bound to the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class ReorderListView : UserControl
{
    public ReorderListView()
    {
        InitializeComponent();
    }
}
