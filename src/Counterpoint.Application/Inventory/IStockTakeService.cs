using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The stock take use case (task P2-T10, SRS FR-4 stock take, AC-10): generate a count sheet with
/// frozen system quantities, record physical counts across as many sessions as it takes, then
/// either post the corrections as one batch or abandon the count without touching stock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mixed authorisation, the same shape as <see cref="Settings.ISettings"/>.</b> Starting a
/// count and recording a physical count carry no <see cref="RequiresRoleAttribute"/>: walking the
/// shop with a scanner and typing in what is on the shelf is exactly the kind of operational task
/// "check stock" already is for a cashier session (<see cref="IStockEnquiry"/>'s own remarks).
/// </para>
/// <para>
/// <see cref="PostAsync"/> is owner-only because the task itself says so - "posted as one batch
/// of <c>STOCK_TAKE</c> stock_movement corrections in one transaction, owner-authorised". Nobody
/// but the owner may turn a count into a stock correction that moves money and margin.
/// </para>
/// <para>
/// <see cref="AbandonAsync"/> is owner-only too, by extension of the same reasoning rather than
/// the task's own explicit word: abandoning a completed count that has already found a shrinkage
/// variance discards the only evidence of it, exactly the "used as a shortcut... hiding cost"
/// shrinkage risk the sibling adjustments task calls out for a mutable stock document. Requiring
/// the owner to sign off on discarding a count is the same control as requiring them to sign off
/// on posting one - both are the moment stock evidence either becomes permanent or disappears.
/// </para>
/// <para>
/// Cost and value never appear from a cashier session either, at the projection level rather than
/// behind a role check on the whole call - see <see cref="BuildVarianceReportAsync"/>'s own
/// remarks, the same split <see cref="IStockEnquiry.FindByVariantIdAsync"/> already draws
/// (CLAUDE.md invariant 8).
/// </para>
/// </remarks>
public interface IStockTakeService
{
    /// <summary>Every stock take, newest first.</summary>
    public Task<IReadOnlyList<StockTakeSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One stock take with its lines, or null when it does not exist.</summary>
    public Task<StockTakeRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a count sheet: allocates the stock take's number, resolves the scope to a set of
    /// active variants, and freezes each one's current stock position as <c>system_qty</c>, all in
    /// one transaction (CLAUDE.md invariant 4).
    /// </summary>
    /// <remarks>
    /// Refuses when the resolved scope shares so much as one variant with any currently-<c>OPEN</c>
    /// stock take's own already-resolved scope, before the number is allocated - two OPEN counts of
    /// the same stock would each later post their own variance for it and the corrections would
    /// silently sum. Two OPEN stock takes with genuinely non-overlapping scopes (different
    /// categories, different racks) are unaffected and continue to run concurrently, exactly as
    /// before - see
    /// <see cref="Counterpoint.Application.Abstractions.Persistence.IStockTakeStore.ListOpenVariantSetsAsync"/>'s
    /// own remarks.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">
    /// Nobody is signed in, the scope is not <c>ALL</c>/<c>CATEGORY:&lt;id&gt;</c>/
    /// <c>BRAND:&lt;id&gt;</c>/<c>LOCATION:&lt;rack&gt;</c>, it matches no active variant, or it
    /// overlaps a currently-<c>OPEN</c> stock take's own scope on at least one variant.
    /// </exception>
    public Task<StartedStockTake> StartAsync(
        StartStockTakeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one line's physical count. Callable repeatedly, across as many sessions as it
    /// takes, for as long as the stock take stays <c>OPEN</c> - a later call for the same variant
    /// overwrites the previous count rather than accumulating (task P2-T10 "partial saving across
    /// sessions").
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Nobody is signed in, the counted quantity is negative, or the stock take does not exist, is
    /// not <c>OPEN</c>, or has no line for that variant.
    /// </exception>
    public Task<RecordedStockTakeCount> RecordCountAsync(
        RecordStockTakeCountCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// System versus counted, quantity and value, sorted by absolute value impact descending
    /// (task P2-T10). <see cref="StockTakeVarianceLine.Value"/> is null for anything but an owner
    /// session - cost is owner-only information, stripped here at the projection rather than by
    /// gating the whole call, the same split <see cref="IStockEnquiry"/> draws for
    /// <c>StockEnquiryResult.CostAvg</c>.
    /// </summary>
    public Task<StockTakeVarianceReport> BuildVarianceReportAsync(
        long stockTakeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts every counted line's variance (never its absolute counted quantity - task P2-T10's
    /// own named risk) as one <c>STOCK_TAKE</c> movement each, through <see cref="IStockLedger"/>,
    /// and closes the stock take, all in one transaction. A line nobody counted is left alone -
    /// posting what was never physically checked would not be "correcting the count", and skipping
    /// it costs nothing, since <see cref="IStockLedger.PostAsync"/> only ever applies a relative
    /// adjustment to whatever balance is current at posting time: a sale rung up between the
    /// freeze and the post is still reflected correctly (task P2-T10 "handle items sold during the
    /// count").
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Nobody is signed in, or the stock take does not exist or is not <c>OPEN</c>.
    /// </exception>
    [RequiresRole(Role.Owner)]
    public Task<PostedStockTake> PostAsync(
        PostStockTakeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons an <c>OPEN</c> stock take. Posts nothing to the stock movement ledger and leaves
    /// every balance untouched - the count sheet and its lines stay on record, only the status
    /// changes (task P2-T10 "abandon path that posts nothing").
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Nobody is signed in, no reason was given, or the stock take does not exist or is not
    /// <c>OPEN</c>.
    /// </exception>
    [RequiresRole(Role.Owner)]
    public Task<AbandonedStockTake> AbandonAsync(
        AbandonStockTakeCommand command,
        CancellationToken cancellationToken = default);
}
