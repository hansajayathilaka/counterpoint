namespace Counterpoint.Application.Settings;

/// <summary>
/// Which visual theme the till trades in (SRS UI-13, NFR-U4, task P3-T10).
/// </summary>
/// <remarks>
/// Stored as <see cref="SettingKeys.UiThemeVariant"/>. This layer only names the choice; mapping
/// it to Avalonia's own theme-variant type, and applying it to a running window, is
/// <c>Counterpoint.Ui</c>'s job - this project may not reference Avalonia at all
/// (CLAUDE.md "Project boundaries").
/// </remarks>
public enum UiThemeVariant
{
    /// <summary>Follow the operating system's own light/dark setting. The default.</summary>
    System = 0,

    /// <summary>Always light, regardless of what the operating system is set to.</summary>
    Light = 1,

    /// <summary>Always dark, regardless of what the operating system is set to.</summary>
    Dark = 2,
}
