using Avalonia;
using Avalonia.Controls;
using Counterpoint.Ui.ViewModels.Settings;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// Task P3-T19's extraction of <c>SettingsWindow.axaml</c>'s old <see cref="TabControl"/> content -
/// the nine settings groups plus its own Save/Undo/status bar - into a plain
/// <see cref="UserControl"/> the back-office shell's content pane can swap in, rather than a
/// separate popup window (SRS FR-10, UI-05, UI-11, UI-13, UI-16, NFR-S2, AC-24). Hosts the exact
/// same nine group views <c>SettingsWindow</c> did, unchanged - <see cref="SettingsViewModel"/> and
/// every child settings viewmodel are untouched by this task; only this hosting chrome changed.
/// </summary>
/// <remarks>
/// Both <see cref="Settings"/> and <see cref="SelectedSection"/> are plain
/// <see cref="StyledProperty{TValue}"/>s, not a <see cref="Control.DataContext"/> override: every
/// binding inside this control's own markup is declared against <c>#Root</c> (this control)
/// explicitly, never the ambient/inherited <c>DataContext</c>, the same discipline
/// <c>CatalogueSectionContent</c> (task P3-T18) already established - so nothing here depends on
/// what the surrounding <c>BackOfficeShellWindow</c>'s own <c>DataContext</c> happens to be.
/// </remarks>
public partial class SettingsSectionContent : UserControl
{
    public static readonly StyledProperty<SettingsViewModel?> SettingsProperty =
        AvaloniaProperty.Register<SettingsSectionContent, SettingsViewModel?>(nameof(Settings));

    /// <summary>
    /// One of <see cref="ViewModels.BackOfficeShellViewModel.SettingsSectionNames"/>, or null to
    /// show nothing (the shell's content pane hides this control entirely for the Overview and
    /// Catalogue cases).
    /// </summary>
    public static readonly StyledProperty<string?> SelectedSectionProperty =
        AvaloniaProperty.Register<SettingsSectionContent, string?>(nameof(SelectedSection));

    public SettingsSectionContent()
    {
        InitializeComponent();
    }

    /// <summary>The settings screen's own viewmodel - the same instance every group binds under.</summary>
    public SettingsViewModel? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public string? SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }
}
