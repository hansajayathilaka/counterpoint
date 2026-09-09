using System;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes the three append-only tables that make up a bill: <c>sale</c>, <c>sale_line</c> and
/// <c>payment</c> (SAD §7, CLAUDE.md invariant 5).
/// </summary>
/// <remarks>
/// <para>
/// Three methods rather than one, because the order in SAD §7 is part of the contract and a
/// single "save this bill" call would hide it. Every one of them must be called inside the same
/// unit of work; the implementation joins the transaction already open on the flow.
/// </para>
/// <para>
/// There is no update and no delete on <c>sale_line</c> or <c>payment</c> here, and there never
/// will be: a completed bill's lines and tenders are immutable (SRS FR-3.31), and the database
/// enforces that with triggers regardless of what any port offers.
/// <see cref="CancelSaleAsync"/> is the one deliberate exception, on <c>sale</c> itself: the
/// database permits exactly one update to that table - <c>status</c> from
/// <c>COMPLETED</c> to <c>CANCELLED</c>, with <c>cancelled_by</c> and <c>cancelled_at</c>
/// (CLAUDE.md invariant 5) - and this is the one place it happens.
/// </para>
/// <para>
/// The hash chain (CLAUDE.md invariant 6) is the writer's business, not the caller's. It reads
/// the previous <c>row_hash</c> inside the same transaction and computes this row's - the
/// Application layer never sees a hash, because a caller that could choose one could break the
/// chain. <see cref="CancelSaleAsync"/> touches none of the hashed columns (docs/01_DATA_MODEL.md
/// §5), so cancelling a bill never needs to, and never may, recompute its hash.
/// </para>
/// </remarks>
public interface ISaleWriter
{
    /// <summary>Inserts the bill header and returns its id.</summary>
    public Task<long> InsertSaleAsync(NewSale sale, CancellationToken cancellationToken = default);

    /// <summary>Inserts one bill line.</summary>
    public Task InsertSaleLineAsync(long saleId, NewSaleLine line, CancellationToken cancellationToken = default);

    /// <summary>Inserts one tender.</summary>
    public Task InsertPaymentAsync(long saleId, NewTender tender, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a completed bill cancelled (SRS FR-3.34). The bill keeps its number and every other
    /// column exactly as written; only <c>status</c>, <c>cancelled_by</c> and <c>cancelled_at</c>
    /// change.
    /// </summary>
    /// <param name="saleId">The bill to cancel. Must currently be <c>COMPLETED</c>.</param>
    /// <param name="cancelledByUserId">The owner cancelling it.</param>
    /// <param name="cancelledAt">When.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task CancelSaleAsync(
        long saleId,
        long cancelledByUserId,
        DateTimeOffset cancelledAt,
        CancellationToken cancellationToken = default);
}
