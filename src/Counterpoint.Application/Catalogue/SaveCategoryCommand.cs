namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="ICategoryMaintenance"/> needs to create or rename a category (FR-2.20).</summary>
/// <param name="Name">The category's name.</param>
/// <param name="ParentId">Null for a top-level category; otherwise an existing top-level category's id.</param>
public sealed record SaveCategoryCommand(string Name, long? ParentId);
