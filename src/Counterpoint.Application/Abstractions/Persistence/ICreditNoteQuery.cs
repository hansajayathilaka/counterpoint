using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads credit notes back - by number, by customer, or the shop-wide reconciliation figure
/// (task P2-T05 steps 3 and 6). Off a read connection, never inside the write transaction
/// <see cref="ICreditNoteIssuer"/> and <see cref="ICreditNoteRedeemer"/> use (CLAUDE.md's
/// "Dapper for hot reads" convention, same split <c>IReturnableSaleLookup</c> draws for a bill).
/// </summary>
public interface ICreditNoteQuery
{
    /// <summary>The credit note carrying this number, or null if none does.</summary>
    public Task<CreditNoteRecord?> FindByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every credit note issued to this customer, most recently issued first - "does this
    /// customer have store credit", asked at the till whenever they do not have the slip in
    /// hand (docs/01_DATA_MODEL.md's own note on <c>ix_credit_note_customer</c>).
    /// </summary>
    public Task<IReadOnlyList<CreditNoteRecord>> FindByCustomerIdAsync(
        long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issued minus redeemed, across every credit note the shop has ever written (task P2-T05
    /// step 6). The underlying query a Phase 3 report line reads; not a report screen itself.
    /// </summary>
    public Task<CreditNoteReconciliation> ReconcileAsync(CancellationToken cancellationToken = default);
}
