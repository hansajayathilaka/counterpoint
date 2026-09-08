namespace Counterpoint.Application.Catalogue;

/// <summary>
/// One existing product the FR-2.24 duplicate-on-creation check thinks might be the same item as
/// the one about to be created.
/// </summary>
/// <param name="ProductId">The existing product.</param>
/// <param name="ProductName">Its name, for the warning.</param>
/// <param name="BrandName">Its brand, or null.</param>
/// <param name="Score">
/// <see cref="Domain.Catalogue.ProductNameSimilarity.Score"/> between the new name and this one -
/// 1 is identical once normalised.
/// </param>
public sealed record SimilarProductMatch(
    long ProductId,
    string ProductName,
    string? BrandName,
    decimal Score);
