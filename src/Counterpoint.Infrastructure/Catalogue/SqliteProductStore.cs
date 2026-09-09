using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using ProductTypes = Counterpoint.Domain.Catalogue.ProductTypes;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// <c>product</c>, <c>product_variant</c> and <c>product_uom</c>, read and written through the
/// unit of work (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
internal sealed class SqliteProductStore : IProductStore
{
    private const long BaseConversionFactorScaled = 10_000L;

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteProductStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var uomSymbols = await context.Set<Uom>().ToDictionaryAsync(row => row.Id, row => row.Symbol, token)
                    .ConfigureAwait(false);

                var variantCounts = await context.Set<ProductVariant>()
                    .GroupBy(row => row.ProductId)
                    .Select(group => new { ProductId = group.Key, Count = group.Count() })
                    .ToDictionaryAsync(row => row.ProductId, row => row.Count, token)
                    .ConfigureAwait(false);

                var products = await context.Set<Product>().OrderBy(row => row.Code).ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<ProductSummaryRecord> result = [.. products.Select(product => new ProductSummaryRecord(
                    product.Id,
                    product.Code,
                    product.Name,
                    ProductTypes.Parse(product.Type),
                    uomSymbols.GetValueOrDefault(product.BaseUomId, string.Empty),
                    variantCounts.GetValueOrDefault(product.Id, 0),
                    product.Active))];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductDuplicateCandidate>> ListForDuplicateCheckAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var brandNames = await context.Set<Brand>().ToDictionaryAsync(row => row.Id, row => row.Name, token)
                    .ConfigureAwait(false);

                var rows = await context.Set<Product>()
                    .Where(row => row.Active)
                    .Select(row => new { row.Id, row.Name, row.BrandId })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<ProductDuplicateCandidate> result = [.. rows.Select(row => new ProductDuplicateCandidate(
                    row.Id,
                    row.Name,
                    row.BrandId,
                    row.BrandId is { } brandId ? brandNames.GetValueOrDefault(brandId) : null))];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<ProductRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var product = await context.Set<Product>().FirstOrDefaultAsync(row => row.Id == id, token)
                    .ConfigureAwait(false);

                return product is null ? null : await ToRecordAsync(context, product, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsWithCodeAsync(string code, long? excludingId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                return await context.Set<Product>().AnyAsync(
                    row => row.Code == code && (excludingId == null || row.Id != excludingId),
                    token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(SaveProductCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var now = DateTimeOffset.UtcNow;

                var product = ToRow(command, now);
                context.Add(product);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                // The base product_uom row, created in the same transaction as the product
                // itself, so a product can never exist without one (docs/01_DATA_MODEL.md §8,
                // "at least one" half of the base-unit guard - the half no CHECK can express).
                context.Add(new ProductUom
                {
                    ProductId = product.Id,
                    UomId = command.BaseUomId,
                    ConversionFactor = BaseConversionFactorScaled,
                    SellingPrice = null,
                    IsBase = true,
                });
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return product.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(long id, SaveProductCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Product>().FirstOrDefaultAsync(p => p.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no product row with id {id}.");

                row.Code = command.Code;
                row.Name = command.Name;
                row.NameAlt = command.NameAlt;
                row.CategoryId = command.CategoryId;
                row.BrandId = command.BrandId;
                row.Type = ProductTypes.ToToken(command.Type);
                row.TaxClassId = command.TaxClassId;
                row.Location = command.Location;
                row.NonReturnable = command.NonReturnable;
                row.WarrantyDays = command.WarrantyDays;
                row.Notes = command.Notes;
                row.MaxDiscountRate = command.MaxDiscountRate;
                row.UpdatedAt = DateTimeOffset.UtcNow;

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

                var row = await context.Set<Product>().FirstOrDefaultAsync(p => p.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no product row with id {id}.");

                row.Active = active;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductVariantRecord>> ListVariantsAsync(long productId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<ProductVariant>()
                    .Where(row => row.ProductId == productId)
                    .OrderBy(row => row.Sku)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<ProductVariantRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<ProductVariantRecord?> FindVariantByIdAsync(long variantId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<ProductVariant>().FirstOrDefaultAsync(v => v.Id == variantId, token)
                    .ConfigureAwait(false);

                return row is null ? null : ToRecord(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsWithSkuAsync(string sku, long? excludingId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                return await context.Set<ProductVariant>().AnyAsync(
                    row => row.Sku == sku && (excludingId == null || row.Id != excludingId),
                    token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateVariantAsync(long productId, SaveProductVariantCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = ToRow(productId, command, DateTimeOffset.UtcNow);
                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> CreateVariantsAsync(
        long productId,
        IReadOnlyList<SaveProductVariantCommand> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var now = DateTimeOffset.UtcNow;

                var rows = commands.Select(command => ToRow(productId, command, now)).ToList();

                foreach (var row in rows)
                {
                    context.Add(row);
                }

                // One statement for every variant in the matrix - the "one operation" FR-2.6 asks
                // for, not sixty round trips.
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                IReadOnlyList<long> ids = [.. rows.Select(row => row.Id)];
                return ids;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateVariantAsync(long variantId, SaveProductVariantCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<ProductVariant>().FirstOrDefaultAsync(v => v.Id == variantId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no variant row with id {variantId}.");

                row.Sku = command.Sku;
                row.Attributes = SerializeAttributes(command.Attributes);
                row.Price = command.Price;

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetVariantActiveAsync(long variantId, bool active, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<ProductVariant>().FirstOrDefaultAsync(v => v.Id == variantId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no variant row with id {variantId}.");

                row.Active = active;
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductUomRecord>> ListUomOptionsAsync(long productId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var uomSymbols = await context.Set<Uom>().ToDictionaryAsync(row => row.Id, row => row.Symbol, token)
                    .ConfigureAwait(false);

                var rows = await context.Set<ProductUom>()
                    .Where(row => row.ProductId == productId)
                    .OrderByDescending(row => row.IsBase)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<ProductUomRecord> result =
                    [.. rows.Select(row => ToRecord(row, uomSymbols.GetValueOrDefault(row.UomId, string.Empty)))];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<ProductUomRecord?> FindUomOptionByIdAsync(long uomOptionId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<ProductUom>().FirstOrDefaultAsync(u => u.Id == uomOptionId, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                var symbol = await context.Set<Uom>()
                    .Where(u => u.Id == row.UomId)
                    .Select(u => u.Symbol)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false);

                return ToRecord(row, symbol ?? string.Empty);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> AddUomOptionAsync(long productId, SaveProductUomCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new ProductUom
                {
                    ProductId = productId,
                    UomId = command.UomId,
                    ConversionFactor = command.Conversion.ToScaled(),
                    SellingPrice = command.SellingPrice,
                    IsBase = false,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateUomOptionAsync(long uomOptionId, SaveProductUomCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<ProductUom>().FirstOrDefaultAsync(u => u.Id == uomOptionId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no product unit row with id {uomOptionId}.");

                if (row.IsBase)
                {
                    throw new InvalidOperationException(
                        "The base unit cannot be edited through AddUomOptionAsync/UpdateUomOptionAsync.");
                }

                row.UomId = command.UomId;
                row.ConversionFactor = command.Conversion.ToScaled();
                row.SellingPrice = command.SellingPrice;

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RemoveUomOptionAsync(long uomOptionId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<ProductUom>().FirstOrDefaultAsync(u => u.Id == uomOptionId, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return true;
                }

                if (row.IsBase)
                {
                    throw new InvalidOperationException(
                        "The base unit cannot be removed through RemoveUomOptionAsync.");
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

    private static async Task<ProductRecord> ToRecordAsync(PosDbContext context, Product product, CancellationToken token)
    {
        var categoryName = product.CategoryId is { } categoryId
            ? await context.Set<Category>().Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(token).ConfigureAwait(false)
            : null;

        var brandName = product.BrandId is { } brandId
            ? await context.Set<Brand>().Where(b => b.Id == brandId).Select(b => b.Name).FirstOrDefaultAsync(token).ConfigureAwait(false)
            : null;

        var baseUomSymbol = await context.Set<Uom>()
            .Where(u => u.Id == product.BaseUomId)
            .Select(u => u.Symbol)
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false) ?? string.Empty;

        var taxClassName = await context.Set<TaxClass>()
            .Where(t => t.Id == product.TaxClassId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false) ?? string.Empty;

        return new ProductRecord(
            product.Id,
            product.Code,
            product.Name,
            product.NameAlt,
            product.CategoryId,
            categoryName,
            product.BrandId,
            brandName,
            product.BaseUomId,
            baseUomSymbol,
            ProductTypes.Parse(product.Type),
            product.TaxClassId,
            taxClassName,
            product.Location,
            product.NonReturnable,
            product.WarrantyDays,
            product.Notes,
            product.MaxDiscountRate,
            product.Active);
    }

    /// <inheritdoc />
    public Task<Money?> FindCostAvgAsync(long productId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // A narrow projection, not the whole product row: this exists only to answer the
                // below-cost check without putting cost on the shared, not-owner-only ProductRecord
                // (CLAUDE.md invariant 8).
                var row = await context.Set<Product>()
                    .Where(product => product.Id == productId)
                    .Select(product => new { product.CostAvg })
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false);

                return row?.CostAvg;
            },
            cancellationToken);

    private static Product ToRow(SaveProductCommand command, DateTimeOffset now) => new()
    {
        Code = command.Code,
        Name = command.Name,
        NameAlt = command.NameAlt,
        CategoryId = command.CategoryId,
        BrandId = command.BrandId,
        BaseUomId = command.BaseUomId,
        Type = ProductTypes.ToToken(command.Type),
        TaxClassId = command.TaxClassId,
        CostAvg = Money.Zero,
        ReorderLevel = 0,
        ReorderQty = 0,
        Location = command.Location,
        NonReturnable = command.NonReturnable,
        MinSellQty = 0,
        MaxDiscountRate = command.MaxDiscountRate,
        WarrantyDays = command.WarrantyDays,
        Notes = command.Notes,
        ImagePath = null,
        Active = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static ProductVariant ToRow(long productId, SaveProductVariantCommand command, DateTimeOffset now) => new()
    {
        ProductId = productId,
        Sku = command.Sku,
        Attributes = SerializeAttributes(command.Attributes),
        Price = command.Price,
        Active = true,
        CreatedAt = now,
    };

    private static ProductVariantRecord ToRecord(ProductVariant row) => new(
        row.Id, row.ProductId, row.Sku, DeserializeAttributes(row.Attributes), row.Price, row.Active);

    private static ProductUomRecord ToRecord(ProductUom row, string uomSymbol) => new(
        row.Id, row.ProductId, row.UomId, uomSymbol, UomConversion.FromScaled(row.ConversionFactor), row.SellingPrice, row.IsBase);

    private static string SerializeAttributes(IReadOnlyDictionary<string, string> attributes) =>
        attributes.Count == 0 ? "{}" : JsonSerializer.Serialize(attributes);

    private static Dictionary<string, string> DeserializeAttributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
}
