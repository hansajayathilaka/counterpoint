using Avalonia;
using Avalonia.Controls;
using Counterpoint.Ui.ViewModels.Catalogue;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// Task P3-T18's extraction of <c>CatalogueWindow.axaml</c>'s old <see cref="TabControl"/> content
/// into a plain <see cref="UserControl"/> the back-office shell's content pane can swap in, rather
/// than a separate popup window (SRS UI-11, UI-16, NFR-S2, AC-24). Hosts the exact same eight tab
/// views <c>CatalogueWindow</c> did, unchanged - <see cref="ViewModels.Catalogue.CatalogueViewModel"/>
/// and every child tab viewmodel are untouched by this task.
/// </summary>
/// <remarks>
/// Both <see cref="Catalogue"/> and <see cref="SelectedSection"/> are plain
/// <see cref="StyledProperty{TValue}"/>s, not a <see cref="Control.DataContext"/> override: every
/// binding inside this control's own markup is declared against <c>#Root</c> (this control)
/// explicitly, never the ambient/inherited <c>DataContext</c>, so nothing here depends on what the
/// surrounding <c>BackOfficeShellWindow</c>'s own <c>DataContext</c> happens to be.
/// </remarks>
public partial class CatalogueSectionContent : UserControl
{
    public static readonly StyledProperty<CatalogueViewModel?> CatalogueProperty =
        AvaloniaProperty.Register<CatalogueSectionContent, CatalogueViewModel?>(nameof(Catalogue));

    /// <summary>
    /// One of <see cref="ViewModels.BackOfficeShellViewModel.CatalogueSectionNames"/>, or null to
    /// show nothing (the shell's content pane hides this control entirely for the Overview case).
    /// </summary>
    public static readonly StyledProperty<string?> SelectedSectionProperty =
        AvaloniaProperty.Register<CatalogueSectionContent, string?>(nameof(SelectedSection));

    public CatalogueSectionContent()
    {
        InitializeComponent();
    }

    /// <summary>The catalogue screen's own viewmodel - the same instance every tab binds under.</summary>
    public CatalogueViewModel? Catalogue
    {
        get => GetValue(CatalogueProperty);
        set => SetValue(CatalogueProperty, value);
    }

    public string? SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }
}
