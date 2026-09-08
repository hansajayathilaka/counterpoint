namespace Counterpoint.Application.Settings;

/// <summary>
/// The <c>audit_log.action</c> and <c>entity_type</c> values a settings change is filed under
/// (SRS FR-10.9, NFR-S8).
/// </summary>
public static class SettingsAuditActions
{
    /// <summary>One setting was changed.</summary>
    public const string SettingChanged = "SETTING_CHANGED";

    /// <summary>The first-run wizard completed.</summary>
    public const string FirstRunCompleted = "FIRST_RUN_COMPLETED";

    /// <summary>The table a settings change happened to.</summary>
    public const string SettingEntityType = "app_setting";
}
