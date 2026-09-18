using Avalonia.Styling;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.Styles;

/// <summary>
/// The one place <see cref="UiThemeVariant"/> becomes Avalonia's own <see cref="ThemeVariant"/>
/// and back (task P3-T10). <c>Counterpoint.Application</c> may not reference Avalonia at all
/// (CLAUDE.md "Project boundaries"), so the setting itself is the shop's own three-way enum, and
/// this mapping is the only place that ever writes an Avalonia type out of it.
/// </summary>
internal static class ThemeVariantMapping
{
    /// <summary>
    /// <see cref="UiThemeVariant.System"/> maps to <see cref="ThemeVariant.Default"/> - Avalonia's
    /// own "follow the operating system" value, not a third named theme of its own.
    /// </summary>
    internal static ThemeVariant ToAvalonia(UiThemeVariant variant) => variant switch
    {
        UiThemeVariant.Light => ThemeVariant.Light,
        UiThemeVariant.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
