using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The stock valuation report (task P2-T11 "Do this" #2, SRS FR-4 reorder / FR-9.7): every
/// variant's current quantity on hand valued at its own moving-average cost, and the grand total.
/// </summary>
/// <remarks>
/// Owner-only, the same as <see cref="IBulkBreakValueConservationQuery"/> and
/// <see cref="IAdjustmentHistoryQuery"/>: every figure here is cost-derived, and a cashier has no
/// reason to see how much capital the shop's shelves hold (CLAUDE.md invariant 8). The concrete
/// reader lives in <c>Counterpoint.Reporting</c> and is decorated with this role check inside that
/// project's own dependency-injection extension, the same way <c>Counterpoint.Backup</c> decorates
/// its own owner-only services rather than the composition root doing it (neither project's
/// internal implementation type is nameable from <c>Counterpoint.App</c>).
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IStockValuationQuery
{
    /// <summary>
    /// Every <c>stock_balance</c> row valued at its own <c>cost_avg</c>, plus the grand total.
    /// <see cref="StockValuationReport.TotalValue"/> is computed from
    /// <c>SUM(stock_balance.qty_base * stock_balance.cost_avg)</c> across the whole table, with no
    /// filter - it ties to that raw sum exactly, with no rounding step at all: <see cref="Money"/>
    /// is not quantised until it is written to the database (<see cref="Money.ToScaled"/>), and
    /// this report never writes one back (task P2-T11's own "Done when": "ties to
    /// sum(stock_balance.qty_base × cost_avg) exactly").
    /// </summary>
    public Task<StockValuationReport> GetValuationAsync(CancellationToken cancellationToken = default);
}

/// <summary>The stock valuation report's result (task P2-T11 "Do this" #2).</summary>
/// <param name="Lines">Every variant currently in <c>stock_balance</c>, highest value first.</param>
/// <param name="TotalValue">The grand total - see <see cref="IStockValuationQuery.GetValuationAsync"/>.</param>
public sealed record StockValuationReport(
    IReadOnlyList<StockValuationLine> Lines,
    Money TotalValue);

/// <summary>One variant's current stock valued at its own moving-average cost.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="ProductDescription">The product's name.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol.</param>
/// <param name="QtyOnHandBase">Current quantity on hand, in the base unit. May be negative (Q-11).</param>
/// <param name="CostAvg">The variant's current moving-average cost, per base unit.</param>
/// <param name="Value">
/// <see cref="QtyOnHandBase"/> times <see cref="CostAvg"/>, exactly - no rounding (see this
/// interface's own remarks on <see cref="Money"/> not being quantised until it is stored). The
/// lines therefore sum to <see cref="StockValuationReport.TotalValue"/> exactly too.
/// </param>
public sealed record StockValuationLine(
    long ProductVariantId,
    string ProductDescription,
    string Sku,
    string BaseUomSymbol,
    Quantity QtyOnHandBase,
    Money CostAvg,
    Money Value);
