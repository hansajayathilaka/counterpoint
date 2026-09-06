using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>credit_note_redemption</c> (docs/01_DATA_MODEL.md §6). See Schema/README.md.
/// </summary>
internal sealed class CreditNoteRedemption
{
    public long Id { get; set; }

    public long CreditNoteId { get; set; }

    public long SaleId { get; set; }

    public Money Amount { get; set; }

    public DateTimeOffset RedeemedAt { get; set; }
}
