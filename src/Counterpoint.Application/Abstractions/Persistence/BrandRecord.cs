namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>brand</c> (docs/01_DATA_MODEL.md §3).</summary>
public sealed record BrandRecord(long Id, string Name, bool Active);
