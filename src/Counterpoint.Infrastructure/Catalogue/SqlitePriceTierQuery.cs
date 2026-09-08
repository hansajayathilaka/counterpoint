using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// <c>price_tier</c>, read for <c>Counterpoint.Domain.Pricing.PriceResolver</c>
/// (docs/01_DATA_MODEL.md §3, SRS FR-2.14-FR-2.16).
/// </summary>
internal sealed class SqlitePriceTierQuery : IPriceTierQuery
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqlitePriceTierQuery(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PriceTierCandidate>> ListForVariantAsync(
        long productVariantId,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // price_tier.min_qty carries no uom_id of its own - it is always in the product's
                // base unit (docs/01_DATA_MODEL.md §3) - so the base unit has to be found through
                // the variant to build a Quantity that PriceResolver can compare against the
                // quantity sold without it throwing on a unit mismatch.
                var baseUomId = await context.Set<ProductVariant>()
                    .Where(variant => variant.Id == productVariantId)
                    .Join(context.Set<Product>(), variant => variant.ProductId, product => product.Id, (_, product) => product.BaseUomId)
                    .FirstOrDefaultAsync(token)
                    .ConfigureAwait(false);

                var rows = await context.Set<PriceTier>()
                    .Where(row => row.ProductVariantId == productVariantId)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<PriceTierCandidate> result = [.. rows.Select(row => ToCandidate(row, baseUomId))];
                return result;
            },
            cancellationToken);

    private static PriceTierCandidate ToCandidate(PriceTier row, long baseUomId) => new(
        row.Id,
        CustomerPriceTiers.Parse(row.Tier),
        Quantity.FromScaled(row.MinQty, baseUomId),
        row.Price,
        ParseDate(row.ValidFrom),
        ParseDate(row.ValidTo));

    private static DateOnly? ParseDate(string? value) =>
        value is null ? null : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
