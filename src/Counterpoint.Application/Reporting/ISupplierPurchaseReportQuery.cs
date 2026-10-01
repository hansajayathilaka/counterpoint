using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// Supplier purchase summary (task P3-T06 "Do this" #5, SRS §9 RPT-16): goods received by supplier and
/// by item, with value, and cost-price movement over time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b> (SRS §9 lists RPT-16 for the owner role) - it is all cost.
/// </para>
/// <para>
/// <b>Value</b> is landed cost excluding tax: line subtotal plus the line's share of freight
/// (<c>goods_receipt_line.line_total</c> less its <c>tax</c>); by supplier that is
/// <c>goods_receipt.subtotal + other_cost</c>, so the two tables add up to the same figure. Tax is shown
/// beside it. <b>Cost movement</b> uses <c>unit_cost_base</c>, the landed cost per base unit, in time
/// order. A receipt is dated by the wall-clock date of <c>received_at</c> (the table has no business date).
/// Supplier <i>payables</i> (RPT-17) are not here: the data model has no supplier payment record.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ISupplierPurchaseReportQuery
{
    /// <summary>Goods received in <paramref name="range"/>, optionally for one supplier only.</summary>
    public Task<SupplierPurchaseReport> GetReportAsync(
        ReportDateRange range,
        long? supplierId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One supplier's line of the summary.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierName">Their name.</param>
/// <param name="ReceiptCount">Goods receipts in the range.</param>
/// <param name="Value">Landed cost excluding tax (<c>subtotal + other_cost</c>).</param>
/// <param name="Tax">Tax on the receipts.</param>
/// <param name="Total">The receipts' own <c>total</c> (value plus tax).</param>
public sealed record SupplierPurchaseRow(
    long SupplierId,
    string SupplierName,
    int ReceiptCount,
    Money Value,
    Money Tax,
    Money Total);

/// <summary>One item's line of the summary.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="Description">The product's name.</param>
/// <param name="QtyBase">Quantity received in base units.</param>
/// <param name="Value">Landed cost excluding tax.</param>
/// <param name="AverageUnitCost">Weighted average landed cost per base unit (<paramref name="Value"/> over <paramref name="QtyBase"/>); zero when nothing was received.</param>
/// <param name="FirstUnitCost">The landed cost per base unit on the earliest receipt in the range.</param>
/// <param name="LastUnitCost">The landed cost per base unit on the latest receipt in the range.</param>
/// <param name="CostChange"><paramref name="LastUnitCost"/> minus <paramref name="FirstUnitCost"/>.</param>
/// <param name="CostChangeRate"><paramref name="CostChange"/> over <paramref name="FirstUnitCost"/> as a fraction (0.05 = 5%); zero when the first cost is zero.</param>
public sealed record SupplierPurchaseItemRow(
    long ProductVariantId,
    string Sku,
    string Description,
    Quantity QtyBase,
    Money Value,
    Money AverageUnitCost,
    Money FirstUnitCost,
    Money LastUnitCost,
    Money CostChange,
    decimal CostChangeRate);

/// <summary>One receipt line's cost, for the cost-movement series of an item whose cost changed in the range.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="ReceivedAt">When the goods were received.</param>
/// <param name="SupplierName">Who supplied them.</param>
/// <param name="GrnNo">The goods receipt number.</param>
/// <param name="UnitCostBase">Landed cost per base unit on this receipt.</param>
public sealed record CostMovementPoint(
    long ProductVariantId,
    string Sku,
    DateTimeOffset ReceivedAt,
    string SupplierName,
    string GrnNo,
    Money UnitCostBase);

/// <summary>The supplier purchase summary for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="SupplierId">The supplier filter, or null for all suppliers.</param>
/// <param name="BySupplier">One row per supplier, largest value first.</param>
/// <param name="ByItem">One row per item, largest value first.</param>
/// <param name="CostMovement">Every receipt-line cost point, oldest first, for items whose landed cost differs between receipts in the range.</param>
/// <param name="TotalValue">Sum of the supplier rows' value; equals the item rows' total.</param>
/// <param name="TotalTax">Sum of the supplier rows' tax.</param>
/// <param name="TotalPurchases">Sum of the supplier rows' total.</param>
public sealed record SupplierPurchaseReport(
    ReportDateRange Range,
    long? SupplierId,
    IReadOnlyList<SupplierPurchaseRow> BySupplier,
    IReadOnlyList<SupplierPurchaseItemRow> ByItem,
    IReadOnlyList<CostMovementPoint> CostMovement,
    Money TotalValue,
    Money TotalTax,
    Money TotalPurchases);
