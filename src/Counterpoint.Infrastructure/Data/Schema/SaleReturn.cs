using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>sale_return</c> (docs/01_DATA_MODEL.md §6). APPEND ONLY, hash chained.
/// See Schema/README.md.
/// </summary>
internal sealed class SaleReturn
{
    public long Id { get; set; }

    public string ReturnNo { get; set; } = string.Empty;

    public DateTimeOffset ReturnedAt { get; set; }

    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. See <see cref="Sale.BusinessDate"/>.</summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>Null means unlinked - a return with no receipt (FR-5, elevated risk).</summary>
    public long? OriginalSaleId { get; set; }

    /// <summary>Set when the return is one half of an exchange.</summary>
    public long? ExchangeSaleId { get; set; }

    public long? CustomerId { get; set; }

    public long UserId { get; set; }

    public long ShiftId { get; set; }

    public Money Subtotal { get; set; }

    public Money Tax { get; set; }

    public Money RestockingFee { get; set; }

    public Money TotalRefund { get; set; }

    public string RefundMethod { get; set; } = string.Empty;

    /// <summary>The owner who authorised an override, when one was needed.</summary>
    public long? AuthorisedBy { get; set; }

    public string? Reason { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string RowHash { get; set; } = string.Empty;
}
