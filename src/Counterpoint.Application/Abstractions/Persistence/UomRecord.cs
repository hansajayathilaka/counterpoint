namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>uom</c> (docs/01_DATA_MODEL.md §3).</summary>
public sealed record UomRecord(long Id, string Name, string Symbol, int DecimalPlaces, bool Active);
