using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Purchasing;

/// <summary>
/// <c>goods_receipt</c> and <c>goods_receipt_line</c>, read and written through the unit of work
/// (docs/01_DATA_MODEL.md §4, SRS FR-4.7, FR-4.8, AC-08).
/// </summary>
/// <remarks>
/// Neither table is append-only (CLAUDE.md invariant 5 names exactly which ones are, and these
/// two are not among them). The same shape as <c>SqlitePurchaseOrderStore</c>: <see cref="CreateAsync"/>
/// is the only write, because nothing about a goods receipt is ever edited once posted - a
/// mistake is corrected by a stock adjustment (P2-T08), never by rewriting the receipt.
/// </remarks>
internal sealed class SqliteGoodsReceiptStore : IGoodsReceiptStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteGoodsReceiptStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GoodsReceiptSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var headers = await (
                    from receipt in context.Set<GoodsReceipt>()
                    join supplier in context.Set<Supplier>() on receipt.SupplierId equals supplier.Id
                    join order in context.Set<PurchaseOrder>() on receipt.PurchaseOrderId equals (long?)order.Id into orders
                    from order in orders.DefaultIfEmpty()
                    orderby receipt.Id descending
                    select new
                    {
                        receipt.Id,
                        receipt.GrnNo,
                        SupplierName = supplier.Name,
                        receipt.PurchaseOrderId,
                        PurchaseOrderNo = (string?)order.PoNo,
                        receipt.ReceivedAt,
                        receipt.Total,
                    })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var lineCounts = await context.Set<GoodsReceiptLine>()
                    .GroupBy(line => line.GoodsReceiptId)
                    .Select(group => new { GoodsReceiptId = group.Key, Count = group.Count() })
                    .ToDictionaryAsync(row => row.GoodsReceiptId, row => row.Count, token)
                    .ConfigureAwait(false);

                IReadOnlyList<GoodsReceiptSummaryRecord> result = [.. headers.Select(header => new GoodsReceiptSummaryRecord(
                    header.Id,
                    header.GrnNo,
                    header.SupplierName,
                    header.PurchaseOrderId,
                    header.PurchaseOrderNo,
                    header.ReceivedAt,
                    header.Total,
                    lineCounts.GetValueOrDefault(header.Id)))];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GoodsReceiptRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await LoadAsync(context, id, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(NewGoodsReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (receipt.Lines.Count == 0)
        {
            throw new ArgumentException("A goods receipt must have at least one line.", nameof(receipt));
        }

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new GoodsReceipt
                {
                    GrnNo = receipt.GrnNo,
                    SupplierId = receipt.SupplierId,
                    PurchaseOrderId = receipt.PurchaseOrderId,
                    SupplierInvNo = receipt.SupplierInvoiceNo,
                    ReceivedAt = receipt.ReceivedAt,
                    Subtotal = receipt.Subtotal,
                    Tax = receipt.Tax,
                    OtherCost = receipt.OtherCost,
                    Total = receipt.Total,
                    UserId = receipt.UserId,
                    Note = receipt.Note,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (var line in receipt.Lines)
                {
                    context.Add(new GoodsReceiptLine
                    {
                        GoodsReceiptId = row.Id,
                        ProductVariantId = line.ProductVariantId,
                        Qty = line.Qty.ToScaled(),
                        UomId = line.Qty.UomId,
                        QtyBase = line.QtyBase.ToScaled(),
                        UnitCost = line.UnitCost,
                        UnitCostBase = line.UnitCostBase,
                        Tax = line.Tax,
                        LineTotal = line.LineTotal,
                    });
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <summary>Loads one receipt with its lines, resolving every join a screen or a print needs (SKU, product name, unit symbols).</summary>
    private static async Task<GoodsReceiptRecord?> LoadAsync(PosDbContext context, long id, CancellationToken token)
    {
        var header = await (
            from receipt in context.Set<GoodsReceipt>()
            join supplier in context.Set<Supplier>() on receipt.SupplierId equals supplier.Id
            join order in context.Set<PurchaseOrder>() on receipt.PurchaseOrderId equals (long?)order.Id into orders
            from order in orders.DefaultIfEmpty()
            where receipt.Id == id
            select new
            {
                receipt.Id,
                receipt.GrnNo,
                receipt.SupplierId,
                SupplierName = supplier.Name,
                receipt.PurchaseOrderId,
                PurchaseOrderNo = (string?)order.PoNo,
                receipt.SupplierInvNo,
                receipt.ReceivedAt,
                receipt.Subtotal,
                receipt.Tax,
                receipt.OtherCost,
                receipt.Total,
                receipt.UserId,
                receipt.Note,
            })
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var lineRows = await (
            from line in context.Set<GoodsReceiptLine>()
            where line.GoodsReceiptId == id
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
                line.QtyBase,
                BaseUomSymbol = baseUom.Symbol,
                line.UnitCost,
                line.UnitCostBase,
                line.Tax,
                line.LineTotal,
            })
            .ToListAsync(token)
            .ConfigureAwait(false);

        IReadOnlyList<GoodsReceiptLineRecord> lines = [.. lineRows.Select(row => new GoodsReceiptLineRecord(
            row.Id,
            row.ProductVariantId,
            row.Sku,
            row.Name,
            Quantity.FromScaled(row.Qty, row.UomId),
            row.UomSymbol,
            Quantity.FromScaled(row.QtyBase, row.BaseUomId),
            row.BaseUomSymbol,
            row.UnitCost,
            row.UnitCostBase,
            row.Tax,
            row.LineTotal))];

        return new GoodsReceiptRecord(
            header.Id,
            header.GrnNo,
            header.SupplierId,
            header.SupplierName,
            header.PurchaseOrderId,
            header.PurchaseOrderNo,
            header.SupplierInvNo,
            header.ReceivedAt,
            header.Subtotal,
            header.Tax,
            header.OtherCost,
            header.Total,
            header.UserId,
            header.Note,
            lines);
    }
}
