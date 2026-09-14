using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// Posts a bulk break: stock converted from one packaging form into another, moving value between
/// two different <c>product_variant</c> rows (SRS FR-4.9, AC-09, task P2-T09).
/// </summary>
/// <remarks>
/// <para>
/// <c>internal</c>, exactly like <c>PostAdjustmentHandler</c> and <c>GoodsReceiptService</c> are:
/// the role check on <see cref="IPostBulkBreak"/> only holds if nothing outside this assembly can
/// construct the class it is supposed to be in front of.
/// </para>
/// <para>
/// <b>Movement types and value flow.</b> Three movements can post, all sharing the
/// <c>bulk_break.id</c> read back from <see cref="IBulkBreakStore.CreateAsync"/> as
/// <c>ref_doc_id</c>, under <c>ref_doc_type = "BULK_BREAK"</c>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>BULK_BREAK_OUT</c> - the source variant, negative, valued at the source's own
/// moving-average cost read at the start of this transaction (never invented - the same
/// "value already on the shelf, read once" discipline <c>PostAdjustmentHandler</c> keeps).
/// </description></item>
/// <item><description>
/// <c>BULK_BREAK_IN</c> - the destination variant, positive, at a cost that carries the source's
/// value (plus the wastage's value - see below) across, dividing it over
/// <see cref="BulkBreakCommand.ActualQuantity"/> so <c>StockLedgerMath.Apply</c>'s own,
/// already-tested <c>MovingAverageCost.Recompute</c> call recomputes the destination's average
/// the same way it would for a goods receipt - not a second, hand-rolled formula.
/// </description></item>
/// <item><description>
/// <c>DAMAGE</c> - posted only when wastage was declared, and, deliberately, against the
/// <b>source</b> variant rather than the destination. See <see cref="ComputeWastage"/> for why.
/// </description></item>
/// </list>
/// <para>
/// <b>Why the wastage write-off is valued at the source's cost, not the destination's.</b> The
/// task leaves this open. Targeting the destination is tempting - "the loss is metres of loose
/// cable that never came into being" - but it is not mechanically sound: an outbound movement is
/// always valued at whatever the ledger finds as that variant's own current moving-average cost
/// at the moment it posts (<c>StockLedgerMath.Apply</c>, never a value the caller supplies), and a
/// destination variant can carry stock, and therefore an average, from sources entirely unrelated
/// to this break (an earlier delivery, an earlier break of a different batch). Valuing the wastage
/// there would make what gets written off depend on that unrelated history, not on what this break
/// actually cost - and would make the pairing's own value conservation depend on the destination's
/// balance being zero going in, which nothing guarantees. The source's own moving-average cost, by
/// contrast, is fixed and known for the whole of this transaction: an outbound movement never
/// perturbs the average it snapshots (<c>StockLedgerMath.Apply</c>'s own remarks - "the average
/// itself never moves on the way out"), so <c>BULK_BREAK_OUT</c> and this <c>DAMAGE</c> row,
/// posted back to back against the same source variant, are guaranteed to read the identical cost.
/// Attributing the loss to a proportional slice of the very batch that was broken - "this much of
/// the coil, physically, is what the lost metres came from" - is also the more literal reading of
/// what actually happened: the material was lost while it was still coming out of the source form,
/// before any of it became a destination-form unit.
/// </para>
/// <para>
/// <b>Carrying the wastage's value into <see cref="BulkBreakCommand.ActualQuantity"/>'s own cost,
/// not losing it.</b> Once the wastage's value is fixed (the source's own cost, times the
/// proportional slice of the source it represents), the destination's own <c>BULK_BREAK_IN</c>
/// unit cost is <c>(source value + wastage value) / actual quantity</c> - the surviving units
/// absorb the full cost of the batch that was broken, wastage included, so nothing is silently
/// lost off the books (task P2-T09's own "Risks"). <c>BULK_BREAK_OUT</c> plus <c>BULK_BREAK_IN</c>
/// plus the wastage <c>DAMAGE</c> (when one posts) therefore sum to zero by construction, to
/// within the same sub-scaled-unit rounding every other cost-per-unit figure in this codebase
/// already carries (<c>GoodsReceiptService</c>'s own <c>unitCostBase</c> division is the same
/// class of computation) - CLAUDE.md invariant 2's two rounding points are about a bill's line and
/// header totals, not cost, so no line here is exempt from ordinary decimal division the way a
/// bill line would be.
/// </para>
/// <para>
/// <b>The one division this cannot avoid.</b> <c>unitCostIn</c> is a single, ordinary decimal
/// division - full precision until <c>SqliteStockLedger.PostAsync</c> quantises it to
/// <see cref="Money"/>'s own four decimal places on the way into <c>stock_movement</c>. When
/// <see cref="BulkBreakCommand.ActualQuantity"/> happens to divide the combined value evenly (the
/// common case - a shop dealing in round numbers), that quantisation is exact and the pairing
/// nets to bit-perfect zero, as <c>AC_09_...</c> and the "nice numbers" wastage test in
/// <c>BulkBreakTests</c> both prove by hand. For a quantity that does not divide it evenly, no
/// choice of representation can avoid a residual: a quantity and a cost are each independently
/// quantised to four decimal places (<see cref="Quantity"/>, <see cref="Money"/>), and there is no
/// pair of four-decimal-place numbers whose product exactly reconstructs an arbitrary target for
/// every possible ratio - the same limit every other cost-per-unit division in this codebase
/// already lives with, never specially flagged before now because nothing before this task valued
/// a movement <em>pair</em> against each other closely enough to notice. The residual is bounded
/// by half of <see cref="Money.MoneyScale"/>'s own smallest unit, multiplied by
/// <see cref="BulkBreakCommand.ActualQuantity"/> - for any quantity a hardware shop actually
/// handles, a fraction of the currency's smallest printed unit, invisible at the two decimal
/// places the till itself displays (Q-01).
/// </para>
/// </remarks>
internal sealed class PostBulkBreakHandler : IPostBulkBreak
{
    /// <summary>The <c>stock_movement.ref_doc_type</c> every movement a bulk break posts shares (task P2-T09's own wiring convention).</summary>
    private const string RefDocType = "BULK_BREAK";

    private const string BulkBreakOutToken = "BULK_BREAK_OUT";
    private const string BulkBreakInToken = "BULK_BREAK_IN";
    private const string DamageToken = "DAMAGE";

    private readonly IBulkBreakStore _bulkBreaks;
    private readonly IProductLookup _catalogue;
    private readonly IStockPositionReader _positions;
    private readonly IStockLedger _stock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public PostBulkBreakHandler(
        IBulkBreakStore bulkBreaks,
        IProductLookup catalogue,
        IStockPositionReader positions,
        IStockLedger stock,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bulkBreaks);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bulkBreaks = bulkBreaks;
        _catalogue = catalogue;
        _positions = positions;
        _stock = stock;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<BulkBreakResult> PostAsync(
        BulkBreakCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reason = RequireReason(command.Reason);

        if (command.SourceVariantId == command.DestinationVariantId)
        {
            throw new InvalidOperationException(
                "A bulk break moves stock between two different products - the source and " +
                "destination cannot be the same variant.");
        }

        if (command.SourceQuantity <= 0m)
        {
            throw new InvalidOperationException("The source quantity broken must be greater than zero.");
        }

        if (command.ExpectedQuantity <= 0m)
        {
            throw new InvalidOperationException("The expected output quantity must be greater than zero.");
        }

        if (command.ActualQuantity <= 0m)
        {
            throw new InvalidOperationException("The actual output quantity must be greater than zero.");
        }

        if (command.ActualQuantity > command.ExpectedQuantity)
        {
            throw new InvalidOperationException(
                "The actual output quantity cannot exceed what the break was expected to yield.");
        }

        var actor = RequireSignedIn();
        var source = await RequireVariantAsync(command.SourceVariantId, cancellationToken).ConfigureAwait(false);
        var destination = await RequireVariantAsync(command.DestinationVariantId, cancellationToken).ConfigureAwait(false);
        var occurredAt = command.OccurredAt ?? _timeProvider.GetLocalNow();

        var sourceQtyBase = Quantity.FromDecimal(command.SourceQuantity, source.BaseUomId);
        var expectedQtyBase = Quantity.FromDecimal(command.ExpectedQuantity, destination.BaseUomId);
        var actualQtyBase = Quantity.FromDecimal(command.ActualQuantity, destination.BaseUomId);
        var wastageQtyBase = expectedQtyBase - actualQtyBase;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var sourcePosition = await _positions.FindAsync(source.ProductVariantId, token).ConfigureAwait(false);
                var sourceCostAvg = sourcePosition?.CostAvg ?? Money.Zero;

                var totalValue = sourceCostAvg.Multiply(sourceQtyBase.Value);

                var wastage = ComputeWastage(sourceQtyBase, expectedQtyBase, wastageQtyBase, sourceCostAvg, source.BaseUomId);

                var unitCostIn = (totalValue + (wastage?.Value ?? Money.Zero)).Divide(actualQtyBase.Value);

                var bulkBreakId = await _bulkBreaks.CreateAsync(
                    new NewBulkBreak(
                        source.ProductVariantId,
                        destination.ProductVariantId,
                        sourceQtyBase,
                        expectedQtyBase,
                        actualQtyBase,
                        wastageQtyBase,
                        totalValue,
                        reason,
                        actor.Id,
                        occurredAt),
                    token).ConfigureAwait(false);

                // The one door stock is ever allowed through (CLAUDE.md invariant 3).
                await _stock.PostAsync(
                    new StockPosting(
                        source.ProductVariantId,
                        BulkBreakOutToken,
                        sourceQtyBase.Negate(),
                        sourceCostAvg,
                        RefDocType,
                        bulkBreakId,
                        actor.Id,
                        occurredAt,
                        reason),
                    token).ConfigureAwait(false);

                await _stock.PostAsync(
                    new StockPosting(
                        destination.ProductVariantId,
                        BulkBreakInToken,
                        actualQtyBase,
                        unitCostIn,
                        RefDocType,
                        bulkBreakId,
                        actor.Id,
                        occurredAt,
                        reason),
                    token).ConfigureAwait(false);

                if (wastage is { } declaredWastage)
                {
                    await _stock.PostAsync(
                        new StockPosting(
                            source.ProductVariantId,
                            DamageToken,
                            declaredWastage.SourceQty.Negate(),
                            sourceCostAvg,
                            RefDocType,
                            bulkBreakId,
                            actor.Id,
                            occurredAt,
                            reason),
                        token).ConfigureAwait(false);
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        occurredAt,
                        actor.Id,
                        BulkBreakAuditActions.Posted,
                        BulkBreakAuditActions.EntityType,
                        bulkBreakId,
                        AfterJson: AuditPayload(
                            source.ProductVariantId, destination.ProductVariantId, sourceQtyBase,
                            actualQtyBase, wastageQtyBase, totalValue),
                        Reason: reason),
                    token).ConfigureAwait(false);

                return new BulkBreakResult(
                    bulkBreakId,
                    source.ProductVariantId,
                    destination.ProductVariantId,
                    sourceQtyBase,
                    sourceCostAvg,
                    totalValue,
                    expectedQtyBase,
                    actualQtyBase,
                    unitCostIn,
                    wastageQtyBase,
                    wastage?.Value);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The wastage write-off, when there is one: the equivalent slice of the <em>source</em>
    /// quantity the lost destination-form units came from, and the value that slice carries at the
    /// source's own (fixed, already-known) moving-average cost. See the class remarks for why the
    /// wastage is attributed to the source rather than the destination.
    /// </summary>
    /// <remarks>
    /// <see cref="Quantity"/>'s <c>ToScaled()</c>/<c>FromScaled()</c> round trip here, deliberately,
    /// before <see cref="WastagePosting.Value"/> is computed from it: that is the exact figure that
    /// will be persisted on the <c>DAMAGE</c> row once <c>SqliteStockLedger.PostAsync</c> calls the
    /// same <c>ToScaled()</c> on the way to the database, so the value used to size
    /// <c>BULK_BREAK_IN</c>'s own cost, a few lines up in <see cref="PostAsync"/>, matches what the
    /// ledger will actually record - not a higher-precision figure that then drifts once rounded a
    /// second time on its way into <c>stock_movement</c>.
    /// </remarks>
    private static WastagePosting? ComputeWastage(
        Quantity sourceQtyBase,
        Quantity expectedQtyBase,
        Quantity wastageQtyBase,
        Money sourceCostAvg,
        long sourceUomId)
    {
        if (!wastageQtyBase.IsPositive)
        {
            return null;
        }

        var wastageRatio = wastageQtyBase.Value / expectedQtyBase.Value;
        var sourceQtyRaw = sourceQtyBase.Multiply(wastageRatio);
        var sourceQty = Quantity.FromScaled(sourceQtyRaw.ToScaled(), sourceUomId);

        var value = sourceCostAvg.Multiply(sourceQty.Value);

        return new WastagePosting(sourceQty, value);
    }

    private AuthenticatedUser RequireSignedIn() =>
        _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A bulk break records who posted it, so sign in before breaking stock.");

    private async Task<CatalogueItem> RequireVariantAsync(long productVariantId, CancellationToken cancellationToken)
    {
        var item = await _catalogue.FindByVariantIdAsync(productVariantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Product variant {productVariantId} does not exist or is not active."));

        if (ProductTypes.PostsNoStockMovement(item.ProductType))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{item.Description} does not carry stock ({ProductTypes.ToToken(item.ProductType)}). There is nothing to break."));
        }

        return item;
    }

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidOperationException(
                "A bulk break needs a reason - it is the shop's only record of why the stock was repackaged.");
        }

        return trimmed;
    }

    private static string AuditPayload(
        long sourceVariantId,
        long destinationVariantId,
        Quantity sourceQtyBase,
        Quantity actualQtyBase,
        Quantity wastageQtyBase,
        Money totalValue) =>
        SecurityAuditJson.Object(
            ("source_variant_id", sourceVariantId),
            ("destination_variant_id", destinationVariantId),
            ("source_qty_base", sourceQtyBase.ToScaled()),
            ("actual_qty_base", actualQtyBase.ToScaled()),
            ("wastage_qty_base", wastageQtyBase.ToScaled()),
            ("total_value", totalValue.ToScaled()));

    /// <summary>The wastage write-off's own quantity (in the source's base unit) and value.</summary>
    private sealed record WastagePosting(Quantity SourceQty, Money Value);
}
