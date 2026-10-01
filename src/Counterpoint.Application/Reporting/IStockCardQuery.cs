using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The stock movement ledger / item stock card (task P3-T06 "Do this" #4, SRS §9 RPT-11): every
/// <c>stock_movement</c> for one variant in a date range, with movement type, quantity, reference
/// document and running balance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b> (SRS §9 lists RPT-11 for the owner role); the rows carry the ledger's unit cost.
/// </para>
/// <para>
/// <b>This report IS the ledger read.</b> It lists the ledger's own rows for one variant, through the
/// <c>ix_movement_variant_time</c> index, and never sums them into a balance other code depends on:
/// the running balance is carried row to row and every row is checked against the
/// <c>balance_after</c> the ledger recorded when the movement posted. Rows are in the ledger's own
/// posting order (<c>stock_movement.id</c>), which is the order the balance chain was built in - usually
/// chronological, but a back-dated posting is not, and is flagged. The opening balance is the
/// <c>balance_after</c> of the movement immediately before the first row (zero when there is none), not a
/// <c>SUM</c>. Each row is checked against <i>its own predecessor in the ledger</i> (in the date range or not),
/// so a healthy ledger reconciles whatever slice of it a date range picks out. Nothing here feeds or rebuilds
/// <c>stock_balance</c> (CLAUDE.md invariant 3).
/// </para>
/// <para>
/// A day is the wall-clock date in the movement's own <c>occurred_at</c> stamp (the stock ledger has no
/// <c>business_date</c> column).
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IStockCardQuery
{
    /// <summary>The stock card for a variant id; null when the variant does not exist.</summary>
    public Task<StockCard?> GetStockCardAsync(
        long productVariantId,
        ReportDateRange range,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stock card for the variant whose SKU or barcode is <paramref name="skuOrBarcode"/>; null when
    /// none matches.
    /// </summary>
    public Task<StockCard?> GetStockCardBySkuAsync(
        string skuOrBarcode,
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>One ledger row on a stock card.</summary>
/// <param name="MovementId">The <c>stock_movement.id</c>.</param>
/// <param name="OccurredAt">When it posted.</param>
/// <param name="MovementType">For example <c>SALE</c>, <c>GRN</c>, <c>RETURN_IN</c>, <c>ADJUSTMENT</c>.</param>
/// <param name="QtyBase">Signed quantity in base units: positive adds stock.</param>
/// <param name="UnitCost">The cost the ledger recorded on the movement, per base unit.</param>
/// <param name="RefDocType">The kind of document that caused it.</param>
/// <param name="RefDocId">That document's id, when there is one.</param>
/// <param name="ReferenceNo">The document's number (bill, goods receipt or return number) when it can be resolved; empty otherwise.</param>
/// <param name="Note">The movement's note (an adjustment's reason, for example).</param>
/// <param name="RunningBalance">The balance this movement should have left: the <c>balance_after</c> of the ledger's immediately preceding movement for the variant (zero before the first ever) plus <paramref name="QtyBase"/>. Equal to "opening plus every row so far" wherever the rows shown are an unbroken run of the ledger.</param>
/// <param name="BalanceAfter">The balance the ledger recorded when this movement posted.</param>
/// <param name="MatchesLedger">True when <paramref name="RunningBalance"/> equals <paramref name="BalanceAfter"/> - this row follows from the ledger row before it.</param>
/// <param name="PostedOutOfDateOrder">True when the ledger row posted just before this one is dated later (for example a back-dated receipt entered after later trading), so this row's opening is not the previous date's close.</param>
/// <param name="FollowsRowsNotShown">True when the ledger row posted just before this one is not the row above it on the card (it is outside the date range), so the two rows are not adjacent in the ledger.</param>
public sealed record StockCardRow(
    long MovementId,
    DateTimeOffset OccurredAt,
    string MovementType,
    Quantity QtyBase,
    Money UnitCost,
    string RefDocType,
    long? RefDocId,
    string ReferenceNo,
    string? Note,
    Quantity RunningBalance,
    Quantity BalanceAfter,
    bool MatchesLedger,
    bool PostedOutOfDateOrder,
    bool FollowsRowsNotShown);

/// <summary>A variant's stock card for a range.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="Description">The product's name.</param>
/// <param name="BaseUomSymbol">The base unit's symbol.</param>
/// <param name="Range">The range covered.</param>
/// <param name="OpeningBalance">Balance just before the first row: the preceding movement's <c>balance_after</c>, or zero.</param>
/// <param name="Rows">Every movement in the range, in posting order (oldest first).</param>
/// <param name="TotalIn">Sum of the positive movement quantities.</param>
/// <param name="TotalOut">Sum of the negative movement quantities, as a negative number.</param>
/// <param name="ClosingBalance">The last row's <c>balance_after</c> as the ledger recorded it; the opening balance when there are no rows.</param>
/// <param name="Reconciles">
/// True when every row <see cref="StockCardRow.MatchesLedger"/>: each recorded <c>balance_after</c> follows from the
/// ledger's previous movement for the variant. (When <paramref name="IsContiguous"/> this implies opening plus the
/// sum of every row's quantity equals <paramref name="ClosingBalance"/>.) False means the ledger chain is broken for
/// this variant - a defect to investigate. A healthy ledger is always true, whatever rows the range selects.
/// </param>
/// <param name="IsContiguous">
/// True when the rows shown are an unbroken run of the ledger (each row's predecessor is the row above it), so
/// opening plus <paramref name="TotalIn"/> plus <paramref name="TotalOut"/> equals <paramref name="ClosingBalance"/>.
/// False when movements posted between the rows shown are dated outside the range (back-dated postings); the
/// totals then cover only the rows shown.
/// </param>
public sealed record StockCard(
    long ProductVariantId,
    string Sku,
    string Description,
    string BaseUomSymbol,
    ReportDateRange Range,
    Quantity OpeningBalance,
    IReadOnlyList<StockCardRow> Rows,
    Quantity TotalIn,
    Quantity TotalOut,
    Quantity ClosingBalance,
    bool Reconciles,
    bool IsContiguous);
