using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Hands out the next serial an internal barcode is generated from (SRS FR-2.10).
/// </summary>
/// <remarks>
/// <para>
/// Not <c>number_sequence</c>: that table's <c>doc_type</c> column carries a CHECK constraint
/// naming exactly the documents the shop issues (<c>SALE</c>, <c>RETURN</c>, <c>CREDIT_NOTE</c>,
/// <c>GRN</c>, <c>PO</c>, <c>SHIFT</c>, <c>STOCK_TAKE</c>, <c>QUOTE</c>), and an internal barcode
/// is not one of them - widening it for a catalogue-numbering concern would be a schema change
/// this task does not otherwise need. This allocator follows the same rule CLAUDE.md invariant 4
/// states for document numbers even though it is not one: one atomic
/// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> against <c>app_setting</c>, inside the
/// caller's transaction, never a read followed by a write and never <c>MAX(serial)+1</c>.
/// </para>
/// <para>
/// A gap is harmless here in a way it is not for a bill number: nothing reconciles internal
/// barcode serials against a printed sequence the way a bill series is reconciled at close of
/// day, so an allocation that is rolled back with the rest of its transaction simply is not
/// reissued, and that is fine.
/// </para>
/// </remarks>
public interface IBarcodeSerialAllocator
{
    /// <summary>The next serial, starting at 1 the first time this is ever called.</summary>
    public Task<long> AllocateAsync(CancellationToken cancellationToken = default);
}
