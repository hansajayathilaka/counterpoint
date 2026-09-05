using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>customer</c> (docs/01_DATA_MODEL.md §5). <c>balance</c> is a projection of the
/// account ledger, not a fact: it gets its rebuild command with credit accounts in P5.
/// See Schema/README.md.
/// </summary>
internal sealed class Customer
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? TaxNo { get; set; }

    /// <summary><c>RETAIL</c> or <c>TRADE</c>.</summary>
    public string Type { get; set; } = "RETAIL";

    public Money CreditLimit { get; set; }

    public Money Balance { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
