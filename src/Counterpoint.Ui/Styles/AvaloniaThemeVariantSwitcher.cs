using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.Styles;

/// <summary>
/// The real <see cref="IThemeVariantSwitcher"/>: sets
/// <see cref="Avalonia.Application.Current"/>'s <c>RequestedThemeVariant</c> (task P3-T10, SRS
/// UI-13, NFR-U4). Every open window re-resolves every <c>DynamicResource</c> brush against the
/// new theme dictionary the instant this runs - no window needs to be closed and reopened, and no
/// restart is needed.
/// </summary>
/// <remarks>
/// A no-op when <see cref="Avalonia.Application.Current"/> is null, which is every plain xUnit
/// process in this solution: nothing in <c>Counterpoint.App</c> ever builds an Avalonia
/// application inside a test run (CLAUDE.md "this codebase's pre-existing no-headless-UI-test
/// convention"), and a settings screen opened by <c>SettingsScreenTests</c> must go on working
/// exactly as it did before this task, not throw a <see cref="System.NullReferenceException"/>
/// the first time its Display tab loads.
/// </remarks>
public sealed class AvaloniaThemeVariantSwitcher : IThemeVariantSwitcher
{
    /// <inheritdoc />
    public void Apply(UiThemeVariant variant)
    {
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = ThemeVariantMapping.ToAvalonia(variant);
        }
    }
}
