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

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary><c>customer</c>, read and written through the unit of work (SRS FR-6.1).</summary>
internal sealed class SqliteCustomerStore : ICustomerStore
{
    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SqliteCustomerStore(SqliteUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CustomerRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Customer>().OrderBy(row => row.Name).ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<CustomerRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<CustomerRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var row = await context.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false);
                return row is null ? null : ToRecord(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasSalesAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // sale.customer_id carries no foreign key yet (P5-T02), so this is a plain count
                // rather than something the database would refuse on its own.
                return await context.Set<Sale>().AnyAsync(row => row.CustomerId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(NewCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Customer
                {
                    Name = customer.Name,
                    Phone = customer.Phone,
                    Address = customer.Address,
                    TaxNo = customer.TaxNo,
                    Type = customer.Type,
                    CreditLimit = customer.CreditLimit,
                    Balance = Money.Zero,
                    Active = true,
                    CreatedAt = _timeProvider.GetLocalNow(),
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(long id, NewCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no customer row with id {id}.");

                row.Name = customer.Name;
                row.Phone = customer.Phone;
                row.Address = customer.Address;
                row.TaxNo = customer.TaxNo;
                row.Type = customer.Type;
                row.CreditLimit = customer.CreditLimit;

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

                var row = await context.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no customer row with id {id}.");

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

                var row = await context.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, token)
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

    private static CustomerRecord ToRecord(Customer row) => new(
        row.Id, row.Name, row.Phone, row.Address, row.TaxNo, row.Type, row.CreditLimit, row.Balance, row.Active);
}
