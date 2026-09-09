namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// What a bulk price update targets (SRS FR-2.19: "by category, brand or supplier"). Every filter
/// given narrows the set further (AND, not OR) - null leaves that dimension unfiltered.
/// </summary>
/// <param name="CategoryId">
/// Products filed directly under this category. Categories are two levels deep (FR-2.20); a
/// parent category matches only products filed on the parent itself, not its children - simple
/// to explain at the counter, and a shop that wants both runs the update twice.
/// </param>
/// <param name="BrandId">Products of this brand.</param>
/// <param name="SupplierId">Products linked to this supplier through <c>product_supplier</c>.</param>
public sealed record BulkPriceQueryFilter(long? CategoryId, long? BrandId, long? SupplierId);
