namespace Counterpoint.Application.Settings;

/// <summary>
/// One setting flattened for storage: its key, its text and its <c>value_type</c>.
/// </summary>
/// <remarks>
/// The shape <see cref="SettingsSerializer.ToRows"/> produces and the shape the diff in
/// <c>SettingsService</c> compares. It carries no user - who changed it is added when it becomes
/// a <c>SettingWrite</c>, because the same row means something different depending on who wrote
/// it.
/// </remarks>
/// <param name="Key">The <c>app_setting.key</c>, from <see cref="SettingKeys"/>.</param>
/// <param name="Value">The stored text.</param>
/// <param name="ValueType">
/// <c>STRING</c>, <c>INT</c>, <c>MONEY</c> or <c>BOOL</c>. Money is the scaled integer
/// convention - amount times 10 000, never a floating type (CLAUDE.md invariant 1).
/// </param>
public sealed record SettingRow(string Key, string Value, string ValueType);
