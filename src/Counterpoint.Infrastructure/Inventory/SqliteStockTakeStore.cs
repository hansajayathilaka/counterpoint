using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// <c>stock_take</c> and <c>stock_take_line</c>, read and written through the unit of work
/// (docs/01_DATA_MODEL.md §4, SRS FR-4 stock take, AC-10, task P2-T10).
/// </summary>
/// <remarks>
/// <para>
/// Neither table is append-only (CLAUDE.md invariant 5 names exactly which ones are, and these
/// two are not among them), the same shape as <c>SqlitePurchaseOrderStore</c>.
/// </para>
/// <para>
/// The scope-matching query in <see cref="StartAsync"/> resolves variants from <c>product</c> and
/// <c>product_variant</c> alone. What each one currently has on hand is read through
/// <see cref="IStockPositionReader"/> instead of the projection's own EF entity type - the same
/// hand-written-SQL door <c>SqliteStockPositionReader</c> itself uses - because
/// <c>ArchitectureTests.OnlyTheStockLedgerFamilyWritesTheStockProjectionOrTheLedger</c> (CLAUDE.md
/// invariant 3) allows nothing else in this assembly to reference that entity type at all, read or
/// write.
/// </para>
/// </remarks>
internal sealed class SqliteStockTakeStore : IStockTakeStore
{
    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly IStockPositionReader _stockPositions;

    public SqliteStockTakeStore(SqliteUnitOfWork unitOfWork, IStockPositionReader stockPositions)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(stockPositions);
        _unitOfWork = unitOfWork;
        _stockPositions = stockPositions;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StockTakeSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var headers = await context.Set<StockTake>()
                    .OrderByDescending(take => take.Id)
                    .Select(take => new
                    {
                        take.Id,
                        take.StockTakeNo,
                        take.Scope,
                        take.Status,
                        take.StartedAt,
                        take.CompletedAt,
                    })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var lineCounts = await context.Set<StockTakeLine>()
                    .GroupBy(line => line.StockTakeId)
                    .Select(group => new { StockTakeId = group.Key, Count = group.Count() })
                    .ToDictionaryAsync(row => row.StockTakeId, row => row.Count, token)
                    .ConfigureAwait(false);

                IReadOnlyList<StockTakeSummaryRecord> result = [.. headers.Select(header => new StockTakeSummaryRecord(
                    header.Id,
                    header.StockTakeNo,
                    header.Scope,
                    header.Status,
                    header.StartedAt,
                    header.CompletedAt,
                    lineCounts.GetValueOrDefault(header.Id)))];

                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StockTakeRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await LoadAsync(context, id, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<long> StartAsync(NewStockTake request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = StockTakeScope.Parse(request.Scope);

        // Resolved and read before the transaction opens - the same NFR-P3 discipline
        // GoodsReceiptService keeps for its own pricing: catalogue reads and the current-position
        // reads through IStockPositionReader are not part of the write, and the writer lock should
        // be held for the insert alone.
        var variantIds = await ResolveScopeAsync(scope, cancellationToken).ConfigureAwait(false);

        if (variantIds.Count == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Scope '{request.Scope}' matches no active product variant. There is nothing to count."));
        }

        var frozenQty = new Dictionary<long, long>(variantIds.Count);
        foreach (var variantId in variantIds)
        {
            var position = await _stockPositions.FindAsync(variantId, cancellationToken).ConfigureAwait(false);
            frozenQty[variantId] = position?.QtyBase.ToScaled() ?? 0L;
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var header = new StockTake
                {
                    Scope = scope.ToToken(),
                    StockTakeNo = request.StockTakeNo,
                    StartedAt = request.StartedAt,
                    Status = StockTakeStatuses.OpenToken,
                    UserId = request.UserId,
                };

                context.Add(header);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (var variantId in variantIds)
                {
                    context.Add(new StockTakeLine
                    {
                        StockTakeId = header.Id,
                        ProductVariantId = variantId,
                        SystemQty = frozenQty[variantId],
                        CountedQty = null,
                        Variance = null,
                        CountedAt = null,
                    });
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return header.Id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every active, sellable variant a scope matches - <c>product</c> and <c>product_variant</c> alone.</summary>
    private async Task<IReadOnlyList<long>> ResolveScopeAsync(StockTakeScope scope, CancellationToken cancellationToken) =>
        await _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var variants =
                    from variant in context.Set<ProductVariant>()
                    join product in context.Set<Product>() on variant.ProductId equals product.Id
                    where variant.Active && product.Active
                    select new { variant.Id, Product = product };

                variants = scope.Kind switch
                {
                    StockTakeScopeKind.All => variants,
                    StockTakeScopeKind.Category => variants.Where(row => row.Product.CategoryId == scope.Id),
                    StockTakeScopeKind.Brand => variants.Where(row => row.Product.BrandId == scope.Id),
                    StockTakeScopeKind.Location => variants.Where(row => row.Product.Location == scope.Location),
                    _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "There are exactly four stock take scope kinds."),
                };

                IReadOnlyList<long> ids = await variants.Select(row => row.Id).ToListAsync(token).ConfigureAwait(false);
                return ids;
            },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockTakeLineRecord?> RecordCountAsync(
        long stockTakeId,
        long productVariantId,
        decimal countedQty,
        DateTimeOffset countedAt,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var take = await context.Set<StockTake>()
                    .FirstOrDefaultAsync(t => t.Id == stockTakeId, token)
                    .ConfigureAwait(false);

                if (take is null || !string.Equals(take.Status, StockTakeStatuses.OpenToken, StringComparison.Ordinal))
                {
                    return null;
                }

                var line = await context.Set<StockTakeLine>()
                    .FirstOrDefaultAsync(
                        l => l.StockTakeId == stockTakeId && l.ProductVariantId == productVariantId,
                        token)
                    .ConfigureAwait(false);

                if (line is null)
                {
                    return null;
                }

                var product = await (
                    from variant in context.Set<ProductVariant>()
                    join p in context.Set<Product>() on variant.ProductId equals p.Id
                    join baseUom in context.Set<Uom>() on p.BaseUomId equals baseUom.Id
                    where variant.Id == productVariantId
                    select new { variant.Sku, p.Name, p.BaseUomId, BaseUomSymbol = baseUom.Symbol })
                    .FirstAsync(token)
                    .ConfigureAwait(false);

                var countedScaled = Quantity.FromDecimal(countedQty, product.BaseUomId).ToScaled();

                line.CountedQty = countedScaled;
                line.Variance = countedScaled - line.SystemQty;
                line.CountedAt = countedAt;

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return ToRecord(line, product.Sku, product.Name, product.BaseUomId, product.BaseUomSymbol);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetStatusAsync(
        long stockTakeId,
        string status,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var take = await context.Set<StockTake>().FirstOrDefaultAsync(t => t.Id == stockTakeId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"There is no stock take row with id {stockTakeId}."));

                take.Status = status;
                take.CompletedAt = completedAt;

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <summary>Loads one stock take with its lines, resolving every join a screen or a report needs (SKU, product name, unit symbol).</summary>
    private static async Task<StockTakeRecord?> LoadAsync(PosDbContext context, long id, CancellationToken token)
    {
        var header = await context.Set<StockTake>()
            .Where(take => take.Id == id)
            .Select(take => new
            {
                take.Id,
                take.StockTakeNo,
                take.Scope,
                take.Status,
                take.StartedAt,
                take.CompletedAt,
                take.UserId,
            })
            .FirstOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var lineRows = await (
            from line in context.Set<StockTakeLine>()
            where line.StockTakeId == id
            join variant in context.Set<ProductVariant>() on line.ProductVariantId equals variant.Id
            join product in context.Set<Product>() on variant.ProductId equals product.Id
            join baseUom in context.Set<Uom>() on product.BaseUomId equals baseUom.Id
            orderby line.Id
            select new
            {
                line.Id,
                line.ProductVariantId,
                variant.Sku,
                product.Name,
                product.BaseUomId,
                BaseUomSymbol = baseUom.Symbol,
                line.SystemQty,
                line.CountedQty,
                line.Variance,
                line.CountedAt,
            })
            .ToListAsync(token)
            .ConfigureAwait(false);

        IReadOnlyList<StockTakeLineRecord> lines = [.. lineRows.Select(row => new StockTakeLineRecord(
            row.Id,
            row.ProductVariantId,
            row.Sku,
            row.Name,
            row.BaseUomId,
            row.BaseUomSymbol,
            Quantity.FromScaled(row.SystemQty, row.BaseUomId),
            row.CountedQty is { } counted ? Quantity.FromScaled(counted, row.BaseUomId) : null,
            row.Variance is { } variance ? Quantity.FromScaled(variance, row.BaseUomId) : null,
            row.CountedAt))];

        return new StockTakeRecord(
            header.Id,
            header.StockTakeNo,
            header.Scope,
            header.Status,
            header.StartedAt,
            header.CompletedAt,
            header.UserId,
            lines);
    }

    private static StockTakeLineRecord ToRecord(
        StockTakeLine line,
        string sku,
        string productName,
        long baseUomId,
        string baseUomSymbol) => new(
            line.Id,
            line.ProductVariantId,
            sku,
            productName,
            baseUomId,
            baseUomSymbol,
            Quantity.FromScaled(line.SystemQty, baseUomId),
            line.CountedQty is { } counted ? Quantity.FromScaled(counted, baseUomId) : null,
            line.Variance is { } variance ? Quantity.FromScaled(variance, baseUomId) : null,
            line.CountedAt);
}
