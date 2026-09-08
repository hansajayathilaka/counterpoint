namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of <c>category</c> (docs/01_DATA_MODEL.md §3, FR-2.20).
/// </summary>
/// <param name="Id">The row id.</param>
/// <param name="Name">The category's name, unique within its parent.</param>
/// <param name="ParentId">Null for a top-level category. A child may never itself be a parent.</param>
/// <param name="ParentName">The parent's name, for display. Null when <paramref name="ParentId"/> is null.</param>
/// <param name="Active">Deactivated categories stay for history but are hidden from new use.</param>
public sealed record CategoryRecord(long Id, string Name, long? ParentId, string? ParentName, bool Active);
