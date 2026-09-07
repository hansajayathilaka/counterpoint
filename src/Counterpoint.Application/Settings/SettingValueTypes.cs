namespace Counterpoint.Application.Settings;

/// <summary>
/// The four <c>app_setting.value_type</c> tokens this framework uses, out of the five the
/// column's CHECK constraint allows (docs/01_DATA_MODEL.md §8).
/// </summary>
/// <remarks>
/// <c>JSON</c> is allowed by the schema but unused here on purpose: a setting stored as a JSON
/// blob is a setting nothing can diff, audit or migrate one field at a time. Every FR-10 setting
/// is one scalar in one row.
/// </remarks>
public static class SettingValueTypes
{
    /// <summary>Text, and every enum token and time-of-day.</summary>
    public const string Text = "STRING";

    /// <summary>
    /// A whole number. Also carries a rate: a <c>Percentage</c> or a <c>TaxRate</c> is stored as
    /// its fraction scaled by 10 000, the same convention every rate column in the schema uses
    /// (docs/01_DATA_MODEL.md §1). 10 000 is 100%.
    /// </summary>
    public const string Number = "INT";

    /// <summary>
    /// An amount, as the scaled 64-bit integer <c>Money.ToScaled()</c> produces - amount times
    /// 10 000 (CLAUDE.md invariant 1, SRS DM-01).
    /// </summary>
    public const string Money = "MONEY";

    /// <summary><c>true</c> or <c>false</c>, lower case.</summary>
    public const string Boolean = "BOOL";
}
