namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>app_setting</c> (docs/01_DATA_MODEL.md §8). Keyed on <c>key</c>, not on an id.
/// See Schema/README.md.
/// </summary>
internal sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Always TEXT. <see cref="ValueType"/> says how to read it.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary><c>STRING</c>, <c>INT</c>, <c>MONEY</c>, <c>BOOL</c> or <c>JSON</c>.</summary>
    public string ValueType { get; set; } = string.Empty;

    public long? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
