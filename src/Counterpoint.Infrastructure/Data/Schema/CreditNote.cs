using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>credit_note</c> (docs/01_DATA_MODEL.md §6). See Schema/README.md.</summary>
internal sealed class CreditNote
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public long SaleReturnId { get; set; }

    public long? CustomerId { get; set; }

    public Money AmountIssued { get; set; }

    public Money AmountRemaining { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. Null means it never expires.</summary>
    public string? ExpiresOn { get; set; }

    public string Status { get; set; } = string.Empty;
}
