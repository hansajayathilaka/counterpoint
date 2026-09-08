namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One <c>app_setting</c> row as it sits on disk (docs/01_DATA_MODEL.md §8): always TEXT, with
/// <paramref name="ValueType"/> saying how to read it.
/// </summary>
/// <param name="Value">The stored text.</param>
/// <param name="ValueType">
/// <c>STRING</c>, <c>INT</c>, <c>MONEY</c>, <c>BOOL</c> or <c>JSON</c> - the column's CHECK
/// constraint allows nothing else.
/// </param>
public sealed record StoredSetting(string Value, string ValueType);
