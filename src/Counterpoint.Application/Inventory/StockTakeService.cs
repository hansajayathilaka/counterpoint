using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The stock take use case (task P2-T10, SRS FR-4 stock take, AC-10). See
/// <see cref="IStockTakeService"/>'s own remarks for the authorisation split.
/// </summary>
/// <remarks>
/// <c>internal</c>, the same reason every other service the composition root wraps in
/// <c>RoleAuthorisation.Decorate</c> is (<c>GoodsReceiptService</c>'s own remarks): the method-level
/// role check on <see cref="PostAsync"/> and <see cref="AbandonAsync"/> only holds if nothing
/// outside this assembly can construct the class those checks are supposed to be in front of.
/// </remarks>
internal sealed class StockTakeService : IStockTakeService
{
    /// <summary>The <c>number_sequence.doc_type</c> a stock take is numbered from.</summary>
    private const string StockTakeDocumentType = "STOCK_TAKE";

    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> a posted correction uses.</summary>
    private const string StockTakeMovementType = "STOCK_TAKE";

    private readonly IStockTakeStore _store;
    private readonly IStockLedger _stock;
    private readonly IStockPositionReader _stockPositions;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public StockTakeService(
        IStockTakeStore store,
        IStockLedger stock,
        IStockPositionReader stockPositions,
        IDocumentNumberAllocator numbers,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(stockPositions);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _stock = stock;
        _stockPositions = stockPositions;
        _numbers = numbers;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StockTakeSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<StockTakeRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _store.FindByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<StartedStockTake> StartAsync(
        StartStockTakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireSignedIn();

        // Parsed and canonicalised before the transaction opens - a malformed scope must not
        // consume a document number (CLAUDE.md invariant 4's "a failed sale consumes its number"
        // rule cuts the other way here: this is refused before anything is allocated at all).
        var scope = StockTakeScope.Parse(command.Scope);

        var startedAt = command.StartedAt ?? _timeProvider.GetLocalNow();
        var businessDate = DateOnly.FromDateTime(startedAt.Date);

        var (id, stockTakeNo) = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Resolved and checked before the number is allocated at all - the same "refused
                // before anything is consumed" point the malformed-scope and no-active-variant
                // refusals above already keep. Two OPEN stock takes with non-overlapping scopes
                // (different categories, different racks) are meant to run concurrently - there is
                // deliberately no ux_one_open_stock_take the way there is a ux_one_open_shift
                // (MigrationRunnerTests' own comment). But two whose scopes share so much as one
                // variant would each later post their own correct-looking variance for it, and the
                // corrections would silently sum, over-adjusting the real balance by roughly double
                // the true variance - genuine stock corruption, not two independently-correct
                // adjustments. IStockLedger.PostAsync's relative-adjustment posting protects against
                // a sale happening *between* one stock take's freeze and its own post; it does
                // nothing for two independent counts of the same stock, so this guard is the
                // Application-layer check for exactly that gap.
                var candidateVariantIds = await _store.ResolveScopeAsync(scope, token).ConfigureAwait(false);
                var candidateVariantSet = new HashSet<long>(candidateVariantIds);

                if (candidateVariantSet.Count > 0)
                {
                    var openTakes = await _store.ListOpenVariantSetsAsync(token).ConfigureAwait(false);
                    var collision = openTakes.FirstOrDefault(
                        open => open.VariantIds.Any(candidateVariantSet.Contains));

                    if (collision is not null)
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"Scope '{scope.ToToken()}' overlaps stock take {collision.StockTakeNo}, "
                            + $"which is still OPEN and covers at least one of the same variants. Post "
                            + $"or abandon it before starting a new count over the same stock."));
                    }
                }

                var stockTakeNo = await _numbers
                    .AllocateAsync(StockTakeDocumentType, businessDate, token)
                    .ConfigureAwait(false);

                var id = await _store.StartAsync(
                    new NewStockTake(scope.ToToken(), stockTakeNo, actor.Id, startedAt),
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        startedAt,
                        actor.Id,
                        StockTakeAuditActions.Started,
                        StockTakeAuditActions.EntityType,
                        id,
                        AfterJson: AuditPayload(stockTakeNo, scope.ToToken())),
                    token).ConfigureAwait(false);

                return (id, stockTakeNo);
            },
            cancellationToken).ConfigureAwait(false);

        var record = await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The stock take was created but could not be read back.");

        return new StartedStockTake(record.Id, record.StockTakeNo, record.Lines.Count);
    }

    /// <inheritdoc />
    public async Task<RecordedStockTakeCount> RecordCountAsync(
        RecordStockTakeCountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireSignedIn();

        if (command.CountedQty < 0m)
        {
            throw new InvalidOperationException("A counted quantity cannot be negative.");
        }

        var countedAt = command.CountedAt ?? _timeProvider.GetLocalNow();

        var line = await _unitOfWork.ExecuteInTransactionAsync(
            token => _store.RecordCountAsync(
                command.StockTakeId,
                command.ProductVariantId,
                command.CountedQty,
                countedAt,
                token),
            cancellationToken).ConfigureAwait(false);

        if (line is null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Stock take {command.StockTakeId} is not open, or has no line for variant "
                + $"{command.ProductVariantId}."));
        }

        return new RecordedStockTakeCount(
            command.StockTakeId,
            command.ProductVariantId,
            line.SystemQty,
            line.CountedQty!.Value,
            line.Variance!.Value);
    }

    /// <inheritdoc />
    public async Task<StockTakeVarianceReport> BuildVarianceReportAsync(
        long stockTakeId,
        CancellationToken cancellationToken = default)
    {
        var take = await _store.FindByIdAsync(stockTakeId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no stock take with id {stockTakeId}."));

        // Cost is owner-only information (CLAUDE.md invariant 8), stripped at exactly this
        // projection so there is nothing in the result a cashier session could leak - the same
        // split StockEnquiryService draws for StockEnquiryResult.CostAvg.
        var showValue = _session.Role == Role.Owner;

        var withValue = new List<(StockTakeLineRecord Line, Money Value)>(take.Lines.Count);
        foreach (var line in take.Lines)
        {
            var position = await _stockPositions.FindAsync(line.ProductVariantId, cancellationToken)
                .ConfigureAwait(false);
            var costAvg = position?.CostAvg ?? Money.Zero;
            var value = line.Variance is { } variance ? costAvg * variance.Value : Money.Zero;
            withValue.Add((line, value));
        }

        var ordered = withValue
            .OrderByDescending(entry => Math.Abs(entry.Value.ToScaled()))
            .ToArray();

        IReadOnlyList<StockTakeVarianceLine> lines = [.. ordered.Select(entry => new StockTakeVarianceLine(
            entry.Line.ProductVariantId,
            entry.Line.Sku,
            entry.Line.ProductName,
            entry.Line.UomSymbol,
            entry.Line.SystemQty,
            entry.Line.CountedQty,
            entry.Line.Variance,
            showValue ? entry.Value : null))];

        var totalScaled = withValue.Sum(entry => entry.Value.ToScaled());

        return new StockTakeVarianceReport(
            take.Id,
            take.StockTakeNo,
            take.Scope,
            lines,
            showValue ? Money.FromScaled(totalScaled) : null);
    }

    /// <inheritdoc />
    public async Task<PostedStockTake> PostAsync(
        PostStockTakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireSignedIn();

        var take = await _store.FindByIdAsync(command.StockTakeId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no stock take with id {command.StockTakeId}."));

        if (StockTakeStatuses.Parse(take.Status) != StockTakeStatus.Open)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Stock take {take.StockTakeNo} is {take.Status} and cannot be posted."));
        }

        // Only a counted line with a non-zero variance has anything to post. An uncounted line
        // (CountedQty still null) is left alone rather than treated as "counted zero" - posting a
        // correction for a variant nobody actually walked up to and checked would not be
        // "correcting the count" (task P2-T10's own words), it would be inventing one.
        var toPost = take.Lines
            .Where(line => line.CountedQty is not null && line.Variance is { IsZero: false })
            .ToArray();

        // Current moving-average cost per variant, read before the transaction opens - the same
        // NFR-P3 discipline GoodsReceiptService keeps for its own pricing. It is only ever used as
        // the *inbound* movement cost below (StockLedgerMath.Apply ignores it for an outbound
        // movement, snapshotting the shelf cost instead), and passing the current average back in
        // as the cost of "found" stock leaves the average itself unchanged by the correction -
        // exactly the valuation the sibling adjustments task documents ("valued at current
        // cost_avg").
        var costByVariant = new Dictionary<long, Money>();
        foreach (var line in toPost)
        {
            var position = await _stockPositions.FindAsync(line.ProductVariantId, cancellationToken)
                .ConfigureAwait(false);
            costByVariant[line.ProductVariantId] = position?.CostAvg ?? Money.Zero;
        }

        var postedAt = command.PostedAt ?? _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                foreach (var line in toPost)
                {
                    // The variance, never the counted quantity itself (task P2-T10's own named
                    // risk: posting an absolute quantity would overwrite whatever the current
                    // balance is, wiping out any sale made since the freeze). PostAsync reads the
                    // *current* balance inside this transaction and applies this as a relative
                    // adjustment to it, so a sale rung up between the freeze and this post is
                    // still reflected correctly in the final figure.
                    await _stock.PostAsync(
                        new StockPosting(
                            line.ProductVariantId,
                            StockTakeMovementType,
                            line.Variance!.Value,
                            costByVariant[line.ProductVariantId],
                            StockTakeMovementType,
                            take.Id,
                            actor.Id,
                            postedAt,
                            Note: string.Create(
                                CultureInfo.InvariantCulture,
                                $"Stock take {take.StockTakeNo}")),
                        token).ConfigureAwait(false);
                }

                await _store.SetStatusAsync(
                    take.Id,
                    StockTakeStatuses.PostedToken,
                    postedAt,
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        postedAt,
                        actor.Id,
                        StockTakeAuditActions.Posted,
                        StockTakeAuditActions.EntityType,
                        take.Id,
                        AfterJson: AuditPayload(take.StockTakeNo, toPost.Length)),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return new PostedStockTake(take.Id, take.StockTakeNo, toPost.Length, take.Lines.Count - toPost.Length);
    }

    /// <inheritdoc />
    public async Task<AbandonedStockTake> AbandonAsync(
        AbandonStockTakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireSignedIn();

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new InvalidOperationException("Abandoning a stock take needs a reason.");
        }

        var take = await _store.FindByIdAsync(command.StockTakeId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no stock take with id {command.StockTakeId}."));

        if (StockTakeStatuses.Parse(take.Status) != StockTakeStatus.Open)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Stock take {take.StockTakeNo} is {take.Status} and cannot be abandoned."));
        }

        var abandonedAt = command.AbandonedAt ?? _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Nothing is posted here, deliberately: no call to IStockLedger.PostAsync at all
                // (task P2-T10 "abandon path that posts nothing") - only the status moves.
                await _store.SetStatusAsync(
                    take.Id,
                    StockTakeStatuses.AbandonedToken,
                    abandonedAt,
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        abandonedAt,
                        actor.Id,
                        StockTakeAuditActions.Abandoned,
                        StockTakeAuditActions.EntityType,
                        take.Id,
                        Reason: command.Reason),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return new AbandonedStockTake(take.Id, take.StockTakeNo);
    }

    private AuthenticatedUser RequireSignedIn() =>
        _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A stock take records who counted and who posted it, so sign in first.");

    private static string AuditPayload(string stockTakeNo, string scope) =>
        SecurityAuditJson.Object(("stock_take_no", stockTakeNo), ("scope", scope));

    private static string AuditPayload(string stockTakeNo, int movementsPosted) =>
        SecurityAuditJson.Object(
            ("stock_take_no", stockTakeNo),
            ("movements_posted", (long)movementsPosted));
}
