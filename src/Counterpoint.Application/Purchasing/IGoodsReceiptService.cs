using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Purchasing;

/// <summary>
/// Recording goods receipts against a supplier, with or without a preceding purchase order (SRS
/// FR-4.7, FR-4.8, AC-08). Owner only (SRS §3.3 ROLE-2's "purchasing/GRN", AC-17) - the same
/// reasoning <see cref="IPurchaseOrderService"/> already carries.
/// </summary>
/// <remarks>
/// The main inbound stock path (P2-T07's own context note), and the one place UOM conversion and
/// moving-average cost meet on the way in: every line is converted to the product's base unit and
/// costed per base unit before <see cref="Abstractions.Persistence.IStockLedger.PostAsync"/> ever
/// sees it (CLAUDE.md invariant 3, invariant 1).
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IGoodsReceiptService
{
    /// <summary>Every goods receipt, newest first.</summary>
    public Task<IReadOnlyList<GoodsReceiptSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One goods receipt with its lines, or null when it does not exist.</summary>
    public Task<GoodsReceiptRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new goods receipt: converts every line to base units, apportions
    /// <see cref="CreateGoodsReceiptCommand.OtherCost"/> across the lines by value, posts a
    /// <c>GRN</c> stock movement per line (recomputing the moving-average cost), records the
    /// supplier's last cost, advances the linked purchase order's receipt progress and status
    /// when one is named, audits, queues the GRN document for printing, and - once the receipt
    /// has committed - prints shelf labels for the received quantities (SRS FR-2.12, FR-4.7,
    /// FR-4.8, AC-08).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The supplier or the named purchase order does not exist, the named purchase order is
    /// cancelled, there are no lines, a line's product does not sell in the unit named, a line's
    /// quantity is not one that unit's own rules allow (FR-2.1-FR-2.8), a line's unit cost or tax
    /// is negative, or <see cref="CreateGoodsReceiptCommand.OtherCost"/> is negative.
    /// </exception>
    public Task<GoodsReceiptResult> ReceiveAsync(
        CreateGoodsReceiptCommand command,
        CancellationToken cancellationToken = default);
}
