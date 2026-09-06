namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>category</c> (docs/01_DATA_MODEL.md §3). Two levels only (FR-2.20), enforced by the
/// <c>trg_category_two_levels_*</c> triggers rather than by the caller. See Schema/README.md.
/// </summary>
internal sealed class Category
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Null for a top-level category. A child may never itself be a parent.</summary>
    public long? ParentId { get; set; }

    public bool Active { get; set; }
}
