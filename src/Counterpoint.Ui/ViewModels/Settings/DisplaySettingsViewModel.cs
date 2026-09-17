using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;
using Counterpoint.Ui.Styles;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// UI-13, NFR-U4 - which visual theme the till trades in (task P3-T10).
/// </summary>
/// <remarks>
/// <para>
/// <b>The picker applies its choice immediately, and saves it separately.</b> Every keystroke
/// that lands on a different choice calls <see cref="IThemeVariantSwitcher.Apply"/> there and
/// then, through <see cref="OnThemeVariantChoiceChanged"/> - the owner sees every screen repaint
/// before ever pressing <c>Ctrl+S</c>. Whether that choice is kept goes through exactly the same
/// <see cref="Load"/>/<see cref="Apply"/> round trip as every other tab: <c>Ctrl+S</c> persists it
/// to <c>ui.theme_variant</c>, and <c>Ctrl+R</c> (or a re-open) calls <see cref="Load"/> again,
/// which re-applies whatever is actually in force - so an owner who previews Dark and then
/// presses Undo sees the till switch back, not just the box relabel itself.
/// </para>
/// <para>
/// This is the one tab task P3-T10 builds from the token system by name (its own "Do this" #3):
/// every custom colour on <see cref="Views.Settings.DisplaySettingsView"/> is one of the
/// semantic <c>DynamicResource</c> keys in <c>Styles/Tokens.Light.axaml</c> and
/// <c>Styles/Tokens.Dark.axaml</c>, never a hex literal.
/// </para>
/// </remarks>
public sealed partial class DisplaySettingsViewModel : SettingsGroupViewModel
{
    private readonly EnumChoices<UiThemeVariant> _variants = new(
        (UiThemeVariant.System, "Match this computer"),
        (UiThemeVariant.Light, "Light"),
        (UiThemeVariant.Dark, "Dark"));

    private readonly IThemeVariantSwitcher _switcher;

    [ObservableProperty]
    private string _themeVariantChoice = string.Empty;

    /// <summary>Runs the screen with the real, Avalonia-touching switcher.</summary>
    public DisplaySettingsViewModel()
        : this(new AvaloniaThemeVariantSwitcher())
    {
    }

    /// <param name="switcher">
    /// What actually changes the running theme. Taken as a parameter so a test can prove what
    /// this viewmodel asked for without a live Avalonia application behind it.
    /// </param>
    public DisplaySettingsViewModel(IThemeVariantSwitcher switcher)
    {
        ArgumentNullException.ThrowIfNull(switcher);
        _switcher = switcher;
    }

    /// <inheritdoc />
    public override string Title => "Display";

    /// <inheritdoc />
    public override string Requirement => "UI-13";

    /// <summary>Light, Dark, or follow the operating system.</summary>
    public IReadOnlyList<string> ThemeVariantChoices => _variants.Labels;

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ThemeVariantChoice = _variants.Label(snapshot.Display.ThemeVariant);
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with { Display = new DisplaySettings(_variants.Value(ThemeVariantChoice)) };
    }

    /// <summary>
    /// Live, no-restart theme switching (task P3-T10's own "Do this" #3): every value this box can
    /// hold, including the one <see cref="Load"/> fills it with, is applied at once.
    /// </summary>
    partial void OnThemeVariantChoiceChanged(string value) => _switcher.Apply(_variants.Value(value));
}
