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

/// <summary>
/// <c>barcode</c>, read and written through the unit of work (docs/01_DATA_MODEL.md §3, SRS
/// FR-2.9, FR-2.10, FR-2.24).
/// </summary>
/// <remarks>
/// EF, not Dapper: this is the maintenance path (add, generate, reassign the primary flag,
/// remove), which happens rarely and off the sale path. The hot barcode-to-price lookup a scan
/// takes is <see cref="Counterpoint.Infrastructure.Sales.SqliteProductLookup"/>, which reads the
/// same table over a prepared Dapper statement against the <c>ux_barcode</c> unique index and
/// never through this store.
/// </remarks>
internal sealed class SqliteBarcodeStore : IBarcodeStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteBarcodeStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BarcodeRecord>> ListForVariantAsync(long variantId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Barcode>()
                    .Where(row => row.ProductVariantId == variantId)
                    .OrderByDescending(row => row.IsPrimary)
                    .ThenBy(row => row.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<BarcodeRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<BarcodeRecord?> FindByIdAsync(long barcodeId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Barcode>().FirstOrDefaultAsync(b => b.Id == barcodeId, token)
                    .ConfigureAwait(false);

                return row is null ? null : ToRecord(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<BarcodeConflict?> FindConflictAsync(string barcode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Barcode>().FirstOrDefaultAsync(b => b.Value == barcode, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                var variant = await context.Set<ProductVariant>()
                    .FirstOrDefaultAsync(v => v.Id == row.ProductVariantId, token)
                    .ConfigureAwait(false);

                var productName = variant is null
                    ? string.Empty
                    : await context.Set<Product>()
                        .Where(p => p.Id == variant.ProductId)
                        .Select(p => p.Name)
                        .FirstOrDefaultAsync(token)
                        .ConfigureAwait(false) ?? string.Empty;

                return new BarcodeConflict(row.Id, row.ProductVariantId, variant?.Sku ?? string.Empty, productName);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> AddAsync(long variantId, string barcode, bool isPrimary, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Barcode
                {
                    ProductVariantId = variantId,
                    Value = barcode,
                    IsPrimary = isPrimary,
                };

                context.Add(row);

                try
                {
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }
                catch (DbUpdateException exception) when (SqliteErrors.IsUniqueViolation(exception))
                {
                    // The backstop behind IBarcodeMaintenance's pre-check (FR-2.24): two calls
                    // racing for the same barcode is not reachable on this till's single write
                    // connection, but a store must never depend on its caller for correctness.
                    throw new InvalidOperationException(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"'{barcode}' is already attached to another item."));
                }

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearPrimaryAsync(long variantId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Barcode>()
                    .Where(row => row.ProductVariantId == variantId && row.IsPrimary)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                foreach (var row in rows)
                {
                    row.IsPrimary = false;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetPrimaryAsync(long barcodeId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Barcode>().FirstOrDefaultAsync(b => b.Id == barcodeId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no barcode row with id {barcodeId}.");

                row.IsPrimary = true;
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(long barcodeId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Barcode>().FirstOrDefaultAsync(b => b.Id == barcodeId, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                context.Remove(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    private static BarcodeRecord ToRecord(Barcode row) => new(row.Id, row.ProductVariantId, row.Value, row.IsPrimary);
}
