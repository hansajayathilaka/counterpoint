using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The bulk-break value-conservation report (task P2-T09 "Do this" #4: "A validation report:
/// every bulk-break pair nets to zero in value", and the "Done when" it feeds - "the
/// value-conservation report finds no unbalanced pairs").
/// </summary>
/// <remarks>
/// Owner-only, the same as <see cref="IAdjustmentHistoryQuery"/>: every row carries a cost-derived
/// figure, and there is no cashier-facing reason to see it (CLAUDE.md invariant 8).
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IBulkBreakValueConservationQuery
{
    /// <summary>
    /// Every <c>bulk_break</c> whose own <c>BULK_BREAK_OUT</c>/<c>BULK_BREAK_IN</c>/wastage
    /// <c>DAMAGE</c> movements do not sum to zero. Empty when every posted break conserves value -
    /// the passing case this report exists to prove.
    /// </summary>
    public Task<IReadOnlyList<UnbalancedBulkBreak>> FindUnbalancedAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>One bulk break whose movements did not net to zero.</summary>
/// <param name="BulkBreakId">The <c>bulk_break.id</c> - the shared <c>ref_doc_id</c>.</param>
/// <param name="NetValue">
/// What the group's own <c>BULK_BREAK_OUT</c>/<c>BULK_BREAK_IN</c>/<c>DAMAGE</c> movements sum to.
/// Never zero - a group that conserves value is not in the result at all.
/// </param>
public sealed record UnbalancedBulkBreak(long BulkBreakId, Money NetValue);
