using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Purchasing;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Purchasing;

/// <summary>
/// <c>purchase_order</c> and <c>purchase_order_line</c>, read and written through the unit of
/// work (docs/01_DATA_MODEL.md §4, SRS FR-4.5, FR-4.10).
/// </summary>
/// <remarks>
/// Neither table is append-only (CLAUDE.md invariant 5 names exactly which ones are, and these
/// two are not among them), so <see cref="UpdateStatusAsync"/> is an ordinary <c>UPDATE</c>, the
/// same shape as <c>SqliteSupplierStore.SetActiveAsync</c> - not a column-scoped trigger workaround.
/// </remarks>
internal sealed class SqlitePurchaseOrderStore : IPurchaseOrderStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqlitePurchaseOrderStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PurchaseOrderSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // Two queries, not one with a correlated subquery: the per-line total mixes
                // Money and Quantity arithmetic that only LINQ-to-Objects can evaluate, so the
                // lines are materialised first and aggregated in memory - a purchase order list
                // is, by this task's own "keep it light" scope, never more than a shop's own
                // handful of open orders.
                var headers = await (
                    from order in context.Set<PurchaseOrder>()
                    join supplier in context.Set<Supplier>() on order.SupplierId equals supplier.Id
                    orderby order.Id descending
                    select new
                    {
                        order.Id,
                        order.PoNo,
                        SupplierName = supplier.Name,
                        order.OrderedAt,
                        order.ExpectedAt,
                        order.Status,
                    })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var lineRows = await context.Set<PurchaseOrderLine>()
                    .Select(line => new { line.PurchaseOrderId, line.Qty, line.UomId, line.UnitCost })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var linesByOrder = lineRows.ToLookup(row => row.PurchaseOrderId);

                IReadOnlyList<PurchaseOrderSummaryRecord> result = [.. headers.Select(header =>
                {
                    var lines = linesByOrder[header.Id];
                    var totalScaled = lines.Sum(line =>
                        (line.UnitCost * Quantity.FromScaled(line.Qty, line.UomId).Value).ToScaled());

                    return new PurchaseOrderSummaryRecord(
                        header.Id,
                        header.PoNo,
                        header.SupplierName,
                        header.OrderedAt,
                        header.ExpectedAt,
                        header.Status,
                        lines.Count(),
                        Money.FromScaled(totalScaled));
                })];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<PurchaseOrderRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await LoadAsync(context, id, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(NewPurchaseOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Lines.Count == 0)
        {
            throw new ArgumentException("A purchase order must have at least one line.", nameof(order));
        }

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new PurchaseOrder
                {
                    PoNo = order.PoNo,
                    SupplierId = order.SupplierId,
                    OrderedAt = order.OrderedAt,
                    ExpectedAt = order.ExpectedAt,
                    Status = PurchaseOrderStatuses.DraftToken,
                    UserId = order.UserId,
                    Note = order.Note,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (var line in order.Lines)
                {
                    context.Add(new PurchaseOrderLine
                    {
                        PurchaseOrderId = row.Id,
                        ProductVariantId = line.ProductVariantId,
                        Qty = line.Qty.ToScaled(),
                        UomId = line.Qty.UomId,
                        UnitCost = line.UnitCost,
                        QtyReceivedBase = 0,
                    });
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateStatusAsync(
        long id,
        PurchaseOrderStatus status,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<PurchaseOrder>().FirstOrDefaultAsync(o => o.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"There is no purchase order row with id {id}."));

                row.Status = PurchaseOrderStatuses.ToToken(status);
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PurchaseOrderLineReceiptProgress>> FindReceiptProgressAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await (
                    from line in context.Set<PurchaseOrderLine>()
                    where line.PurchaseOrderId == id
                    join variant in context.Set<ProductVariant>() on line.ProductVariantId equals variant.Id
                    join product in context.Set<Product>() on variant.ProductId equals product.Id
                    join uom in context.Set<ProductUom>()
                        on new { product.Id, line.UomId } equals new { Id = uom.ProductId, uom.UomId }
                    select new
                    {
                        line.Qty,
                        line.UomId,
                        line.QtyReceivedBase,
                        product.BaseUomId,
                        uom.ConversionFactor,
                    })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                if (rows.Count == 0)
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"There is no purchase order with id {id}, or it has no lines."));
                }

                IReadOnlyList<PurchaseOrderLineReceiptProgress> result = [.. rows.Select(row =>
                {
                    var ordered = Quantity.FromScaled(row.Qty, row.UomId);
                    var factor = UomConversion.FromScaled(row.ConversionFactor);
                    var orderedBase = Quantity.FromDecimal(ordered.Value * factor.Factor, row.BaseUomId);
                    var receivedBase = Quantity.FromScaled(row.QtyReceivedBase, row.BaseUomId);

                    return new PurchaseOrderLineReceiptProgress(orderedBase, receivedBase);
                })];

                return result;
            },
            cancellationToken);

    /// <summary>Loads one order with its lines, resolving every join a screen or a print needs (SKU, product name, unit symbols).</summary>
    private static async Task<PurchaseOrderRecord?> LoadAsync(PosDbContext context, long id, CancellationToken token)
    {
        var header = await (
            from order in context.Set<PurchaseOrder>()
            join supplier in context.Set<Supplier>() on order.SupplierId equals supplier.Id
            where order.Id == id
            select new
            {
                order.Id,
                order.PoNo,
                order.SupplierId,
                SupplierName = supplier.Name,
                order.OrderedAt,
                order.ExpectedAt,
                order.Status,
                order.UserId,
                order.Note,
            })
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var lineRows = await (
            from line in context.Set<PurchaseOrderLine>()
            where line.PurchaseOrderId == id
            join variant in context.Set<ProductVariant>() on line.ProductVariantId equals variant.Id
            join product in context.Set<Product>() on variant.ProductId equals product.Id
            join baseUom in context.Set<Uom>() on product.BaseUomId equals baseUom.Id
            join uom in context.Set<Uom>() on line.UomId equals uom.Id
            orderby line.Id
            select new
            {
                line.Id,
                line.ProductVariantId,
                variant.Sku,
                product.Name,
                product.BaseUomId,
                line.Qty,
                line.UomId,
                UomSymbol = uom.Symbol,
                line.UnitCost,
                line.QtyReceivedBase,
                BaseUomSymbol = baseUom.Symbol,
            })
            .ToListAsync(token)
            .ConfigureAwait(false);

        IReadOnlyList<PurchaseOrderLineRecord> lines = [.. lineRows.Select(row =>
        {
            var qty = Quantity.FromScaled(row.Qty, row.UomId);
            var lineTotal = row.UnitCost * qty.Value;

            return new PurchaseOrderLineRecord(
                row.Id,
                row.ProductVariantId,
                row.Sku,
                row.Name,
                qty,
                row.UomSymbol,
                row.UnitCost,
                lineTotal,
                Quantity.FromScaled(row.QtyReceivedBase, row.BaseUomId),
                row.BaseUomSymbol);
        })];

        return new PurchaseOrderRecord(
            header.Id,
            header.PoNo,
            header.SupplierId,
            header.SupplierName,
            header.OrderedAt,
            header.ExpectedAt,
            header.Status,
            header.UserId,
            header.Note,
            lines);
    }
}
