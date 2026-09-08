using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads one variant's stock position and recent history for the stock enquiry screen (F11,
/// P1-T07, SRS FR-4).
/// </summary>
/// <remarks>
/// An internal read model, not a cashier DTO - <see cref="StockPosition.CostAvg"/> carries cost,
/// which the Application layer strips before a cashier session ever sees it (CLAUDE.md
/// invariant 8), the same split <c>CatalogueItem</c>/<c>ScannedItem</c> already draws for the
/// sale path.
/// </remarks>
public interface IStockPositionReader
{
    /// <summary>The projected balance for one variant, or null when it has never moved.</summary>
    public Task<StockPosition?> FindAsync(long productVariantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent movements for one variant, newest first.
    /// </summary>
    public Task<IReadOnlyList<StockMovementRecord>> RecentMovementsAsync(
        long productVariantId,
        int take,
        CancellationToken cancellationToken = default);
}

/// <summary>The balance projection's row for one variant.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="QtyBase">Current quantity on hand, in base units.</param>
/// <param name="CostAvg">Current moving-average cost. Owner-only information.</param>
/// <param name="UpdatedAt">When the projection was last written.</param>
public sealed record StockPosition(long ProductVariantId, Quantity QtyBase, Money CostAvg, DateTimeOffset UpdatedAt);

/// <summary>One row of the ledger, as the enquiry screen's history needs it.</summary>
/// <param name="MovementType">For example <c>SALE</c> or <c>GRN</c>.</param>
/// <param name="QtyBase">Signed quantity, in base units.</param>
/// <param name="UnitCost">Cost recorded on the movement. Owner-only information.</param>
/// <param name="RefDocType">What caused it.</param>
/// <param name="RefDocId">The id of that document, or null.</param>
/// <param name="OccurredAt">When.</param>
public sealed record StockMovementRecord(
    string MovementType,
    Quantity QtyBase,
    Money UnitCost,
    string RefDocType,
    long? RefDocId,
    DateTimeOffset OccurredAt);
