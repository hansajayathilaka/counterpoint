namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>brand</c> (docs/01_DATA_MODEL.md §3). See Schema/README.md.</summary>
internal sealed class Brand
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }
}
