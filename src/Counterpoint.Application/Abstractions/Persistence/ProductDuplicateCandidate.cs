namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One active product, as the FR-2.24 duplicate-on-creation check needs it: enough to compare
/// names and brands without the cost of <see cref="ProductRecord"/>'s full detail for every row
/// in the catalogue.
/// </summary>
public sealed record ProductDuplicateCandidate(
    long Id,
    string Name,
    long? BrandId,
    string? BrandName);
