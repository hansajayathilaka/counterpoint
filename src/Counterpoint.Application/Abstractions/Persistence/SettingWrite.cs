namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One setting to be written, with the user who changed it.
/// </summary>
/// <param name="Key">The <c>app_setting.key</c>.</param>
/// <param name="Value">The text to store.</param>
/// <param name="ValueType">
/// <c>STRING</c>, <c>INT</c>, <c>MONEY</c>, <c>BOOL</c> or <c>JSON</c>.
/// </param>
/// <param name="UpdatedBy">
/// Who changed it, or null when the system did it unattended. The same rule the audit row
/// follows: never invent a user id for a change nobody made.
/// </param>
public sealed record SettingWrite(string Key, string Value, string ValueType, long? UpdatedBy);
