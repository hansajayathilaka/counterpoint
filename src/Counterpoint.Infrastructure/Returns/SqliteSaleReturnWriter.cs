using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Returns;

/// <summary>
/// Writes <c>sale_return</c>, <c>sale_return_line</c> and the refund <c>payment</c> row, chains
/// the return's hash, and applies the one permitted <c>sale_line</c> update (CLAUDE.md
/// invariants 5 and 6, task P2-T02).
/// </summary>
/// <remarks>
/// Every method joins the transaction already open on the flow, the same as
/// <c>SqliteSaleWriter</c> - a return's tables commit together or not at all.
/// </remarks>
internal sealed class SqliteSaleReturnWriter : ISaleReturnWriter
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteSaleReturnWriter(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> InsertSaleReturnAsync(NewSaleReturn saleReturn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saleReturn);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // Read inside the transaction, the same reasoning SqliteSaleWriter's own comment
                // gives: the single-writer gate already means nobody else can be appending, but
                // reading the head here means the chain does not depend on that being true.
                var previousHash = await context.Set<SaleReturn>()
                    .OrderByDescending(row => row.Id)
                    .Select(row => row.RowHash)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false) ?? HashChain.GenesisHash;

                var row = new SaleReturn
                {
                    ReturnNo = saleReturn.ReturnNo,
                    ReturnedAt = saleReturn.ReturnedAt,
                    BusinessDate = saleReturn.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    OriginalSaleId = saleReturn.OriginalSaleId,
                    ExchangeSaleId = saleReturn.ExchangeSaleId,
                    CustomerId = saleReturn.CustomerId,
                    UserId = saleReturn.UserId,
                    ShiftId = saleReturn.ShiftId,
                    Subtotal = saleReturn.Subtotal,
                    Tax = saleReturn.Tax,
                    RestockingFee = saleReturn.RestockingFee,
                    TotalRefund = saleReturn.TotalRefund,
                    RefundMethod = saleReturn.RefundMethod,
                    AuthorisedBy = saleReturn.AuthorisedBy,
                    Reason = saleReturn.Reason,
                    PrevHash = previousHash,
                };

                row.RowHash = SaleReturnHashChain.RowHash(previousHash, row);

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task InsertSaleReturnLineAsync(
        long saleReturnId, NewSaleReturnLine line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                context.Add(new SaleReturnLine
                {
                    SaleReturnId = saleReturnId,
                    SaleLineId = line.SaleLineId,
                    ProductVariantId = line.ProductVariantId,
                    QtyBase = line.QuantityBase.ToScaled(),

                    // Snapshots copied verbatim from sale_line, never recomputed here (CLAUDE.md
                    // invariant 10).
                    UnitPrice = line.UnitPrice,
                    UnitCost = line.UnitCost,
                    Tax = line.Tax,
                    LineRefund = line.LineRefund,
                    Reason = line.Reason,
                    Disposition = line.Disposition,
                });

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task IncrementQtyReturnedAsync(
        long saleLineId, Quantity quantityBase, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // Loaded, not attached-and-patched: EF then knows every other column's original
                // value and emits an UPDATE naming only qty_returned, which is what keeps this
                // inside trg_sale_line_restricted_update's guard without trusting this class to
                // have listed the right column by hand - the same reasoning
                // SqliteSaleWriter.CancelSaleAsync gives for sale.status.
                var row = await context.Set<SaleLine>()
                    .FirstOrDefaultAsync(line => line.Id == saleLineId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"sale_line {saleLineId} was not found."));

                // Additive, not absolute - trg_sale_line_qty_returned_bounds is what makes the
                // cumulative bound true (AC-06); this is the increment the caller already checked
                // is safe.
                row.QtyReturned += quantityBase.ToScaled();

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task InsertRefundPaymentAsync(
        long saleReturnId, NewTender tender, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tender);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                context.Add(new Payment
                {
                    SaleId = null,
                    SaleReturnId = saleReturnId,
                    TenderType = tender.TenderType,
                    Amount = tender.Amount,
                    Reference = tender.Reference,
                    PaidAt = tender.PaidAt,
                });

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }
}
