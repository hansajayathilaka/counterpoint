using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// Posts an owner's manual stock correction or damage write-off (SRS FR-4, task P2-T08).
/// </summary>
/// <remarks>
/// <para>
/// <c>internal</c>, exactly like <c>CancelSaleHandler</c> and <c>GoodsReceiptService</c> are: the
/// role check on <see cref="IPostAdjustment"/> only holds if nothing outside this assembly can
/// construct the class the check is supposed to be in front of. The composition root wraps it in
/// <c>RoleAuthorisation.Decorate</c> and only ever hands out <see cref="IPostAdjustment"/>, so a
/// caller cannot reach an undecorated instance (CLAUDE.md invariant 8, SRS NFR-S2, AC-17).
/// </para>
/// <para>
/// <b>There is no separate "adjustment" document.</b> Unlike a GRN or a return, an adjustment is
/// nothing more than the <c>stock_movement</c> row itself - so the audit entry this handler writes
/// is filed against the <c>product_variant</c> that moved (<see cref="AdjustmentAuditActions.EntityType"/>),
/// not against a document id that does not exist.
/// </para>
/// <para>
/// <b>Always valued at today's own moving-average cost, never a new one.</b> Every posting here
/// passes the variant's current <c>cost_avg</c> as <c>StockPosting.UnitCost</c>, whichever
/// direction the movement runs. For an outbound movement <c>StockLedgerMath.Apply</c> ignores it
/// anyway and snapshots the cost already on the shelf (P1-T07); for an inbound one it is what
/// keeps the average exactly where it already stood, by construction of
/// <c>MovingAverageCost.Recompute</c> - the same non-perturbation property
/// <c>CreateUnlinkedReturnHandler</c>'s own remarks rely on for a returned unit re-entering stock.
/// A found box of the same items is not a purchase, and must never be allowed to look like one to
/// the moving average.
/// </para>
/// <para>
/// <b>The current balance is read as late as it can be.</b> <see cref="ReadPositionAsync"/> runs
/// inside the transaction <see cref="AdjustAsync"/>/<see cref="WriteOffDamageAsync"/> open, after
/// <c>BEGIN IMMEDIATE</c> has already taken the single write connection's writer lock
/// (<c>SqliteUnitOfWork</c>'s own remarks: no other writer can even begin until this transaction
/// finishes) - so a target-quantity adjustment's signed delta, and the cost every posting here is
/// valued at, are both computed against a balance nothing else can move out from under them
/// between the read and the <see cref="IStockLedger.PostAsync"/> call a few lines later.
/// </para>
/// </remarks>
internal sealed class PostAdjustmentHandler : IPostAdjustment
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductLookup _catalogue;
    private readonly IStockPositionReader _positions;
    private readonly IStockLedger _stock;
    private readonly IAuditTrail _audit;
    private readonly ISession _session;
    private readonly ISettings _settings;
    private readonly TimeProvider _timeProvider;

    public PostAdjustmentHandler(
        IUnitOfWork unitOfWork,
        IProductLookup catalogue,
        IStockPositionReader positions,
        IStockLedger stock,
        IAuditTrail audit,
        ISession session,
        ISettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _catalogue = catalogue;
        _positions = positions;
        _stock = stock;
        _audit = audit;
        _session = session;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AdjustmentResult> AdjustAsync(
        AdjustmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = RequireReason(command.Reason);
        RequireExactlyOneQuantityMode(command);

        var actor = RequireSignedIn();
        var item = await RequireVariantAsync(command.ProductVariantId, cancellationToken).ConfigureAwait(false);
        var occurredAt = command.OccurredAt ?? _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var (qtyBefore, costAvgBefore) = await ReadPositionAsync(item, token).ConfigureAwait(false);

                var delta = command.QuantityDelta is { } signedDelta
                    ? Quantity.FromDecimal(signedDelta, item.BaseUomId)
                    : Quantity.FromDecimal(command.TargetQuantity!.Value, item.BaseUomId) - qtyBefore;

                RequireNonZero(delta, command.ProductVariantId);

                return await PostAsync(
                    item,
                    AdjustmentTypes.AdjustmentToken,
                    delta,
                    qtyBefore,
                    costAvgBefore,
                    reason,
                    actor.Id,
                    occurredAt,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AdjustmentResult> WriteOffDamageAsync(
        DamageCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = RequireReason(command.Reason);

        if (command.Quantity <= 0m)
        {
            throw new InvalidOperationException("The quantity damaged must be greater than zero.");
        }

        var actor = RequireSignedIn();
        var item = await RequireVariantAsync(command.ProductVariantId, cancellationToken).ConfigureAwait(false);
        var occurredAt = command.OccurredAt ?? _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var (qtyBefore, costAvgBefore) = await ReadPositionAsync(item, token).ConfigureAwait(false);
                var delta = Quantity.FromDecimal(command.Quantity, item.BaseUomId).Negate();

                return await PostAsync(
                    item,
                    AdjustmentTypes.DamageToken,
                    delta,
                    qtyBefore,
                    costAvgBefore,
                    reason,
                    actor.Id,
                    occurredAt,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one movement-posting tail both <see cref="AdjustAsync"/> and
    /// <see cref="WriteOffDamageAsync"/> share: post through <see cref="IStockLedger"/>, audit the
    /// before/after quantity, and build the inbound-value warning (task P2-T08 "Do this" #2/#3,
    /// "Risks").
    /// </summary>
    private async Task<AdjustmentResult> PostAsync(
        CatalogueItem item,
        string movementTypeToken,
        Quantity delta,
        Quantity qtyBefore,
        Money costAvgBefore,
        string reason,
        long userId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        // The one door stock is ever allowed through (CLAUDE.md invariant 3). RefDocType mirrors
        // MovementType and RefDocId is null - there is no separate document row an adjustment
        // belongs to, the same shape CancelSaleHandler's own compensating movement uses.
        await _stock.PostAsync(
            new StockPosting(
                item.ProductVariantId,
                movementTypeToken,
                delta,
                costAvgBefore,
                movementTypeToken,
                RefDocId: null,
                userId,
                occurredAt,
                reason),
            cancellationToken).ConfigureAwait(false);

        // Predicts exactly what IStockLedger.PostAsync just wrote, through the identical pure
        // function it runs internally - not a second, hand-rolled calculation that could drift
        // from it (P1-T07's own StockLedgerMath remarks: the same step both the ledger and the
        // rebuild command replay).
        var step = StockLedgerMath.Apply(qtyBefore, costAvgBefore, delta, costAvgBefore);

        await _audit.RecordAsync(
            new AuditEntry(
                occurredAt,
                userId,
                movementTypeToken == AdjustmentTypes.DamageToken
                    ? AdjustmentAuditActions.DamagePosted
                    : AdjustmentAuditActions.AdjustmentPosted,
                AdjustmentAuditActions.EntityType,
                item.ProductVariantId,
                BeforeJson: PositionPayload(qtyBefore, costAvgBefore),
                AfterJson: PositionPayload(step.QtyAfter, step.CostAvgAfter),
                Reason: reason),
            cancellationToken).ConfigureAwait(false);

        var warning = BuildWarning(delta, costAvgBefore, _settings.Policy.AdjustmentGrnWarningThreshold);

        return new AdjustmentResult(
            item.ProductVariantId,
            movementTypeToken,
            delta,
            qtyBefore,
            step.QtyAfter,
            costAvgBefore,
            warning);
    }

    /// <summary>
    /// The variant's current balance, read as late as possible - see the class remarks for why
    /// this is safe to treat as the live figure rather than a stale one.
    /// </summary>
    /// <remarks>
    /// <see cref="IStockPositionReader.FindAsync"/>'s own <c>Quantity.UomId</c> carries the
    /// product variant id, not the base uom id (a known quirk of that reader, already noted as a
    /// carried should-fix on task P2-T03 - see <c>StockEnquiryService.FindByVariantIdAsync</c>'s
    /// own "only <c>.Value</c> is read" usage). Only <see cref="Quantity.Value"/> is trusted here
    /// for exactly that reason; the quantity is rebuilt against the variant's real base unit
    /// before this handler does any arithmetic with it.
    /// </remarks>
    private async Task<(Quantity QtyBefore, Money CostAvgBefore)> ReadPositionAsync(
        CatalogueItem item, CancellationToken cancellationToken)
    {
        var position = await _positions.FindAsync(item.ProductVariantId, cancellationToken).ConfigureAwait(false);
        var qtyBeforeValue = position?.QtyBase.Value ?? 0m;

        return (Quantity.FromDecimal(qtyBeforeValue, item.BaseUomId), position?.CostAvg ?? Money.Zero);
    }

    /// <summary>
    /// Task P2-T08's own "Risks": an inbound adjustment valued above the configurable threshold
    /// is a warning, never a refusal - CLAUDE.md invariant 7's "never block" spirit, applied here
    /// to a policy nudge rather than a device failure. <see cref="Money.Zero"/> disables the check
    /// entirely, the same "zero means off" convention <c>PolicySettings.CashRefundLimit</c> uses.
    /// </summary>
    private static string? BuildWarning(Quantity delta, Money costAvg, Money threshold)
    {
        if (!delta.IsPositive || !threshold.IsPositive)
        {
            return null;
        }

        var value = costAvg.Multiply(delta.Value);
        if (value <= threshold)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"This inbound adjustment is valued at {value}, above the {threshold} threshold. Consider a goods receipt (GRN) instead, so the supplier and cost are recorded properly.");
    }

    private static string PositionPayload(Quantity qty, Money costAvg) =>
        SecurityAuditJson.Object(
            ("qty_base", qty.ToScaled()),
            ("cost_avg", costAvg.ToScaled()));

    /// <summary>
    /// Mandatory, the same discipline <c>CreateUnlinkedReturnHandler.RequireReason</c> keeps for
    /// its own unconditional reason.
    /// </summary>
    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidOperationException(
                "An adjustment needs a reason - it is the shop's only record of why the count changed.");
        }

        return trimmed;
    }

    private static void RequireExactlyOneQuantityMode(AdjustmentCommand command)
    {
        var hasDelta = command.QuantityDelta is not null;
        var hasTarget = command.TargetQuantity is not null;

        if (hasDelta == hasTarget)
        {
            throw new InvalidOperationException(
                "Give either a quantity change or a target quantity for this adjustment, not both and not neither.");
        }

        if (hasDelta && command.QuantityDelta!.Value == 0m)
        {
            throw new InvalidOperationException(
                "A quantity change of zero is not an adjustment - there is nothing to post.");
        }
    }

    private static void RequireNonZero(Quantity delta, long productVariantId)
    {
        if (delta.IsZero)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Variant {productVariantId} is already at the target quantity given. There is nothing to adjust."));
        }
    }

    private AuthenticatedUser RequireSignedIn() =>
        _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. An adjustment records who made it, so sign in before adjusting stock.");

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
                $"{item.Description} does not carry stock ({ProductTypes.ToToken(item.ProductType)}). There is nothing to adjust."));
        }

        return item;
    }
}
