using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// <see cref="IBulkPriceUpdateService"/> (SRS FR-2.19). Internal, for the same reason every other
/// owner-only catalogue service is: the role check in front of <see cref="IBulkPriceUpdateService"/>
/// only holds if nothing outside this assembly can construct the class it guards.
/// </summary>
internal sealed class BulkPriceUpdateService : IBulkPriceUpdateService
{
    private readonly IPriceQuery _priceQuery;
    private readonly IProductStore _products;
    private readonly IPriceChangeLogStore _priceChangeLog;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public BulkPriceUpdateService(
        IPriceQuery priceQuery,
        IProductStore products,
        IPriceChangeLogStore priceChangeLog,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(priceQuery);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(priceChangeLog);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _priceQuery = priceQuery;
        _products = products;
        _priceChangeLog = priceChangeLog;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<BulkPriceUpdatePreview> PreviewAsync(
        BulkPriceUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = await _priceQuery.FindVariantsAsync(request.Filter, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BulkPriceUpdatePreviewLine> lines = [.. candidates.Select(candidate => ToPreviewLine(candidate, request))];

        return new BulkPriceUpdatePreview(lines);
    }

    /// <inheritdoc />
    public async Task<int> ApplyAsync(BulkPriceUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Re-checked against the current catalogue, not the caller's earlier preview - another
        // edit could have moved a price or a cost since the shop looked at the preview, the same
        // reasoning ProductMaintenanceService.CommitVariantMatrixAsync re-checks its own preview.
        var candidates = await _priceQuery.FindVariantsAsync(request.Filter, cancellationToken).ConfigureAwait(false);
        var lines = candidates.Select(candidate => ToPreviewLine(candidate, request)).ToArray();

        // The domain refuses rather than corrects (engineering guide §4.1): a steep percentage or
        // fixed decrease can carry a price below zero, and ProductMaintenanceService's own
        // single-variant path already refuses that - this path writes product_variant.price
        // directly, so it has to refuse it itself.
        var negative = lines.FirstOrDefault(line => line.NewPrice.IsNegative);
        if (negative is not null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"This update would set '{negative.Sku}' to {negative.NewPrice}, a negative price. Adjust the amount or rate and try again."));
        }

        if (!request.ConfirmBelowCost)
        {
            var belowCost = lines.Where(line => line.BelowCost).ToArray();
            if (belowCost.Length > 0)
            {
                throw new BulkPriceBelowCostWarningException(belowCost);
            }
        }

        var changed = lines.Where(line => line.NewPrice != line.OldPrice).ToArray();
        if (changed.Length == 0)
        {
            return 0;
        }

        var now = _timeProvider.GetLocalNow();
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Bulk price update ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                foreach (var line in changed)
                {
                    var variant = await _products.FindVariantByIdAsync(line.ProductVariantId, token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"Variant {line.ProductVariantId} was removed while this update was running."));

                    await _products.UpdateVariantAsync(
                        line.ProductVariantId,
                        new SaveProductVariantCommand(variant.Sku, variant.Attributes, line.NewPrice, ConfirmBelowCost: true, Reason: request.Reason),
                        token).ConfigureAwait(false);

                    await _priceChangeLog.RecordAsync(
                        new NewPriceChangeLogEntry(line.ProductVariantId, line.OldPrice, line.NewPrice, now, actor.Id, request.Reason),
                        token).ConfigureAwait(false);
                }

                // One audit row for the whole update, not one per variant - the same reasoning
                // CommitVariantMatrixAsync's own audit entry uses for a sixty-variant matrix.
                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        actor.Id,
                        PricingAuditActions.BulkPriceUpdateApplied,
                        CatalogueAuditActions.ProductVariantEntityType,
                        EntityId: null,
                        AfterJson: SecurityAuditJson.Object(
                            ("variant_count", (long)changed.Length),
                            ("category_id", request.Filter.CategoryId?.ToString(CultureInfo.InvariantCulture)),
                            ("brand_id", request.Filter.BrandId?.ToString(CultureInfo.InvariantCulture)),
                            ("supplier_id", request.Filter.SupplierId?.ToString(CultureInfo.InvariantCulture))),
                        Reason: request.Reason),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return changed.Length;
    }

    private static BulkPriceUpdatePreviewLine ToPreviewLine(PriceQueryVariant candidate, BulkPriceUpdateRequest request)
    {
        var newPrice = request.Adjustment.ApplyTo(candidate.Price);

        return new BulkPriceUpdatePreviewLine(
            candidate.ProductVariantId,
            candidate.ProductName,
            candidate.Sku,
            candidate.Price,
            newPrice,
            newPrice <= candidate.Cost);
    }
}
