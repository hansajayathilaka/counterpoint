using Avalonia;
using Avalonia.Controls;
using Counterpoint.Ui.ViewModels.Reports;

namespace Counterpoint.Ui.Views.Reports;

/// <summary>
/// Hosts the four report screens in the back-office shell's content pane (task P3-T05), one visible
/// at a time, exactly as <c>CatalogueSectionContent</c> and <c>SettingsSectionContent</c> host
/// theirs. Both properties are plain styled properties, not a <c>DataContext</c> override: every
/// binding in the markup is declared against <c>#Root</c>, never the ambient <c>DataContext</c>.
/// </summary>
public partial class ReportSectionContent : UserControl
{
    public static readonly StyledProperty<ReportsViewModel?> ReportsProperty =
        AvaloniaProperty.Register<ReportSectionContent, ReportsViewModel?>(nameof(Reports));

    /// <summary>
    /// One of <see cref="ReportsViewModel"/>'s section names, or null to show nothing (the shell's
    /// content pane hides this control entirely unless a report section is selected).
    /// </summary>
    public static readonly StyledProperty<string?> SelectedSectionProperty =
        AvaloniaProperty.Register<ReportSectionContent, string?>(nameof(SelectedSection));

    public ReportSectionContent()
    {
        InitializeComponent();
    }

    /// <summary>The reports container - the same instance every screen binds under.</summary>
    public ReportsViewModel? Reports
    {
        get => GetValue(ReportsProperty);
        set => SetValue(ReportsProperty, value);
    }

    public string? SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }
}
