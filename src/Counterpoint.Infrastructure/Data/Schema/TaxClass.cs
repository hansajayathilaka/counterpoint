using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>tax_class</c>. See Schema/README.md.</summary>
internal sealed class TaxClass
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Stored scaled ×10 000: <c>1500</c> = 15%.</summary>
    public TaxRate Rate { get; set; }

    public bool Active { get; set; }
}
