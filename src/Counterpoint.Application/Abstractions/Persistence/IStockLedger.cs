using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The one door through which stock changes (CLAUDE.md invariant 3, SRS FR-3.12).
/// </summary>
/// <remarks>
/// <para>
/// <c>stock_movement</c> is the append-only truth. The balance projection beside it is written
/// in the same transaction and is rebuildable from the ledger. Nothing may write one without
/// the other, which is why there is a single method here and no separate "adjust the balance"
/// call.
/// </para>
/// <para>
/// The implementation reads the current projection inside the transaction to compute the
/// movement's <c>balance_after</c>. No read path anywhere may sum the ledger instead.
/// </para>
/// <para>
/// The full ledger - moving-average cost, the rebuild command, negative-stock policy - is
/// P1-T07. This is the choke point it will grow into, not a second one.
/// </para>
/// </remarks>
public interface IStockLedger
{
    /// <summary>
    /// Appends one movement and advances the balance projection, in the caller's transaction.
    /// </summary>
    /// <param name="posting">What moved, how much, and why.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task PostAsync(StockPosting posting, CancellationToken cancellationToken = default);
}
