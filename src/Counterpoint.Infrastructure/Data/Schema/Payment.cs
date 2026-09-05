using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>payment</c>. APPEND ONLY. See Schema/README.md.</summary>
internal sealed class Payment
{
    public long Id { get; set; }

    public long? SaleId { get; set; }

    /// <summary>No foreign key yet: <c>sale_return</c> arrives in P2-T02. See §13 of the data model.</summary>
    public long? SaleReturnId { get; set; }

    public string TenderType { get; set; } = string.Empty;

    /// <summary>Negative for a refund out.</summary>
    public Money Amount { get; set; }

    /// <summary>Max 20 characters, PAN-rejecting (NFR-S7).</summary>
    public string? Reference { get; set; }

    public DateTimeOffset PaidAt { get; set; }
}
