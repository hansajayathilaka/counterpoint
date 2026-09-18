using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.Styles;

/// <summary>
/// Applies a theme choice to the running application (task P3-T10, SRS UI-13, NFR-U4).
/// </summary>
/// <remarks>
/// An interface only so <see cref="ViewModels.Settings.DisplaySettingsViewModel"/> can be tested
/// without a live Avalonia application behind it - exactly the same "fake behind an interface"
/// shape as <c>IReceiptPrinter</c>/<c>FileReceiptPrinter</c>
/// (CLAUDE.md "Development platform note"), except every environment this one runs in, including
/// CI, can use the real implementation: <see cref="AvaloniaThemeVariantSwitcher"/> is a no-op
/// wherever <c>Avalonia.Application.Current</c> is null, which is exactly the plain xUnit process
/// every non-UI test in this solution runs in.
/// </remarks>
public interface IThemeVariantSwitcher
{
    /// <summary>Switches the running application to <paramref name="variant"/> immediately.</summary>
    public void Apply(UiThemeVariant variant);
}
