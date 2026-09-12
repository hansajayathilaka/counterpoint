using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Purchasing;

/// <summary>
/// <c>product_supplier</c>, read and written through the unit of work (docs/01_DATA_MODEL.md §3).
/// </summary>
internal sealed class SqliteProductSupplierStore : IProductSupplierStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteProductSupplierStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task UpsertLastCostAsync(
        long productId,
        long supplierId,
        Money lastCost,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var link = await context.Set<ProductSupplier>()
                    .FirstOrDefaultAsync(
                        row => row.ProductId == productId && row.SupplierId == supplierId,
                        token)
                    .ConfigureAwait(false);

                if (link is null)
                {
                    context.Add(new ProductSupplier
                    {
                        ProductId = productId,
                        SupplierId = supplierId,
                        LastCost = lastCost,
                    });
                }
                else
                {
                    link.LastCost = lastCost;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
}
