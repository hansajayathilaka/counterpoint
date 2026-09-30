using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Task P3-T06: one display-only report table. Markup and nothing else - every cell is bound to a
/// <c>ReportTableViewModel</c> that only lays out text the Application layer's DTO already carries.
/// </summary>
public partial class ReportTableView : UserControl
{
    public ReportTableView()
    {
        InitializeComponent();
    }
}
