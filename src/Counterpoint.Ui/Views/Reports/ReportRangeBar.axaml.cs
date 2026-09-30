using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// The shared date-range picker of every report screen (task P3-T05, SRS FR-9.1): a period combo,
/// and two date boxes that appear for a custom range. Markup and nothing else.
/// </summary>
public partial class ReportRangeBar : UserControl
{
    public ReportRangeBar()
    {
        InitializeComponent();
    }
}
