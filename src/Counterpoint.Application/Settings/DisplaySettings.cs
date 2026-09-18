namespace Counterpoint.Application.Settings;

/// <summary>
/// SRS UI-13, NFR-U4 - the "Display" tab (task P3-T10). One field today: which theme variant the
/// till trades in.
/// </summary>
/// <param name="ThemeVariant">Light, Dark, or System (follow the operating system).</param>
public sealed record DisplaySettings(UiThemeVariant ThemeVariant);
