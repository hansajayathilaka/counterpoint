using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>tax_class</c> (docs/01_DATA_MODEL.md §3).</summary>
public sealed record TaxClassRecord(long Id, string Name, TaxRate Rate, bool Active);
