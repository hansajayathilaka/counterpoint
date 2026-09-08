using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary><c>supplier</c>, read and written through the unit of work (SRS FR-6.5).</summary>
internal sealed class SqliteSupplierStore : ISupplierStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteSupplierStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Supplier>().OrderBy(row => row.Name).ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<SupplierRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<SupplierRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var row = await context.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, token)
                    .ConfigureAwait(false);
                return row is null ? null : ToRecord(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasLinksAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                if (await context.Set<ProductSupplier>().AnyAsync(row => row.SupplierId == id, token)
                        .ConfigureAwait(false))
                {
                    return true;
                }

                if (await context.Set<PurchaseOrder>().AnyAsync(row => row.SupplierId == id, token)
                        .ConfigureAwait(false))
                {
                    return true;
                }

                return await context.Set<GoodsReceipt>().AnyAsync(row => row.SupplierId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(NewSupplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Supplier
                {
                    Name = supplier.Name,
                    Contact = supplier.Contact,
                    Phone = supplier.Phone,
                    Address = supplier.Address,
                    TaxNo = supplier.TaxNo,
                    PaymentTerms = supplier.PaymentTerms,
                    Active = true,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(long id, NewSupplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no supplier row with id {id}.");

                row.Name = supplier.Name;
                row.Contact = supplier.Contact;
                row.Phone = supplier.Phone;
                row.Address = supplier.Address;
                row.TaxNo = supplier.TaxNo;
                row.PaymentTerms = supplier.PaymentTerms;

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no supplier row with id {id}.");

                row.Active = active;
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return true;
                }

                context.Remove(row);

                try
                {
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                }
                catch (DbUpdateException exception) when (SqliteErrors.IsForeignKeyViolation(exception))
                {
                    return false;
                }
            },
            cancellationToken);

    private static SupplierRecord ToRecord(Supplier row) => new(
        row.Id, row.Name, row.Contact, row.Phone, row.Address, row.TaxNo, row.PaymentTerms, row.Active);
}
