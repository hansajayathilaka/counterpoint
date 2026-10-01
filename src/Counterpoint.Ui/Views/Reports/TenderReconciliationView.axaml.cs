using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T06: the tender reconciliation screen (owner-only; not SRS RPT-05, which is the Z report). Markup and nothing else - every figure is
/// bound to the matching report viewmodel, itself a pass-through of one Application-layer query.
/// </summary>
public partial class TenderReconciliationView : UserControl
{
    public TenderReconciliationView()
    {
        InitializeComponent();
    }
}
