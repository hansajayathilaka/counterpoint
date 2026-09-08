using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// Answers the stock enquiry screen (F11, P1-T07, SRS FR-4): quantity in base and alternate
/// units, cost for an owner session, and recent movements.
/// </summary>
public sealed class StockEnquiryService : IStockEnquiry
{
    /// <summary>How many recent movements the screen shows.</summary>
    private const int RecentMovementCount = 20;

    private readonly IProductStore _products;
    private readonly IStockPositionReader _stock;
    private readonly ISession _session;

    public StockEnquiryService(IProductStore products, IStockPositionReader stock, ISession session)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(session);

        _products = products;
        _stock = stock;
        _session = session;
    }

    /// <inheritdoc />
    public async Task<StockEnquiryResult?> FindByVariantIdAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        var variant = await _products.FindVariantByIdAsync(productVariantId, cancellationToken)
            .ConfigureAwait(false);
        if (variant is null)
        {
            return null;
        }

        var product = await _products.FindByIdAsync(variant.ProductId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Variant {productVariantId} belongs to product {variant.ProductId}, which does not exist."));

        var uoms = await _products.ListUomOptionsAsync(variant.ProductId, cancellationToken).ConfigureAwait(false);

        var position = await _stock.FindAsync(productVariantId, cancellationToken).ConfigureAwait(false);
        var movements = await _stock.RecentMovementsAsync(productVariantId, RecentMovementCount, cancellationToken)
            .ConfigureAwait(false);

        // Cost is owner-only information, stripped at exactly this projection so there is
        // nothing in the result a cashier session could leak (CLAUDE.md invariant 8).
        var showCost = _session.Role == Role.Owner;

        var qtyBase = position?.QtyBase.Value ?? 0m;

        var alternateUnits = uoms
            .Where(uom => !uom.IsBase)
            .Select(uom => new StockEnquiryUnitQuantity(
                uom.UomId,
                uom.UomSymbol,
                qtyBase / uom.Conversion.Factor))
            .ToArray();

        var recentMovements = movements
            .Select(movement => new StockEnquiryMovement(
                movement.OccurredAt,
                movement.MovementType,
                movement.QtyBase.Value,
                movement.RefDocType,
                movement.RefDocId,
                showCost ? movement.UnitCost : null))
            .ToArray();

        return new StockEnquiryResult(
            productVariantId,
            product.Name,
            variant.Sku,
            product.BaseUomId,
            product.BaseUomSymbol,
            qtyBase,
            showCost ? position?.CostAvg : null,
            position?.UpdatedAt,
            alternateUnits,
            recentMovements);
    }
}
