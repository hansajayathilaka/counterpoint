using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Writes <c>sale</c>, <c>sale_line</c> and <c>payment</c>, and chains the bill's hash
/// (CLAUDE.md invariants 5, 6 and 10).
/// </summary>
/// <remarks>
/// Every method joins the transaction already open on the flow, so the three tables of a bill
/// commit together or not at all. The append-only triggers behind them are what make that true
/// even for a repair session with <c>sqlite3</c>; nothing here is trusted to enforce it.
/// </remarks>
internal sealed class SqliteSaleWriter : ISaleWriter
{
    /// <summary>The only status a bill is ever written with. Cancellation is an update, later.</summary>
    private const string CompletedStatus = "COMPLETED";

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteSaleWriter(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> InsertSaleAsync(NewSale sale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // Read inside the transaction. The single-writer gate already means nobody else
                // can be appending, but reading the head here means the chain does not depend on
                // that being true.
                var previousHash = await context.Set<Sale>()
                    .OrderByDescending(row => row.Id)
                    .Select(row => row.RowHash)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false) ?? HashChain.GenesisHash;

                var row = new Sale
                {
                    BillNo = sale.BillNo,
                    SoldAt = sale.SoldAt,
                    BusinessDate = sale.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CustomerId = null,
                    UserId = sale.UserId,
                    ShiftId = sale.ShiftId,
                    Subtotal = sale.Subtotal,
                    LineDiscount = sale.LineDiscount,
                    BillDiscount = sale.BillDiscount,
                    Tax = sale.Tax,
                    Rounding = sale.Rounding,
                    Total = sale.Total,
                    Cogs = sale.Cogs,
                    Status = CompletedStatus,
                    CancelledBy = null,
                    CancelledAt = null,
                    Note = null,
                    PrevHash = previousHash,
                };

                row.RowHash = SaleHashChain.RowHash(previousHash, row);

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task InsertSaleLineAsync(long saleId, NewSaleLine line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                context.Add(new SaleLine
                {
                    SaleId = saleId,
                    LineNo = line.LineNo,
                    ProductVariantId = line.ProductVariantId,

                    // The three snapshots of CLAUDE.md invariant 10: description, unit_price and
                    // unit_cost as they stood when it was sold. The catalogue moves; a refund
                    // six months from now must not.
                    Description = line.Description,
                    Qty = line.Quantity.ToScaled(),
                    UomId = line.Quantity.UomId,
                    QtyBase = line.QuantityBase.ToScaled(),
                    UnitPrice = line.UnitPrice,
                    Discount = line.Discount,
                    TaxRate = line.TaxRate,
                    Tax = line.Tax,
                    LineTotal = line.LineTotal,
                    UnitCost = line.UnitCost,
                    QtyReturned = 0,
                    Note = null,
                });

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task InsertPaymentAsync(long saleId, NewTender tender, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tender);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                context.Add(new Payment
                {
                    SaleId = saleId,
                    SaleReturnId = null,
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
