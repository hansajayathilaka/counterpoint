using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>stock_take</c> and <c>stock_take_line</c> (docs/01_DATA_MODEL.md §4, SRS
/// FR-4 stock take, AC-10).
/// </summary>
/// <remarks>
/// Neither table is append-only (CLAUDE.md invariant 5 names exactly which tables are, and these
/// two are not among them) - the same class of in-progress document as <c>purchase_order</c> and
/// <c>goods_receipt</c>. Nothing here ever writes the stock movement ledger or its balance
/// projection: the batch of <c>STOCK_TAKE</c> corrections goes through
/// <see cref="IStockLedger"/> alone (CLAUDE.md invariant 3), from
/// <c>Counterpoint.Application.Inventory.StockTakeService.PostAsync</c>.
/// </remarks>
public interface IStockTakeStore
{
    /// <summary>Every stock take, newest first - the list screen's whole read side.</summary>
    public Task<IReadOnlyList<StockTakeSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One stock take with its lines, or null when it does not exist.</summary>
    public Task<StockTakeRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves every active variant <paramref name="request"/>'s scope matches, freezes each
    /// one's current stock balance projection quantity as <c>system_qty</c> (never summed from
    /// <c>stock_movement</c> - CLAUDE.md invariant 3), and inserts the <c>stock_take</c> header
    /// with one <c>stock_take_line</c> per variant, in the caller's transaction - so a stock take
    /// can never exist with no lines, the same "insert header and lines together" discipline
    /// <see cref="IPurchaseOrderStore.CreateAsync"/> and <see cref="IGoodsReceiptStore.CreateAsync"/>
    /// keep.
    /// </summary>
    /// <returns>The new <c>stock_take.id</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The scope matches no active, sellable variant - there would be nothing to count.
    /// </exception>
    public Task<long> StartAsync(NewStockTake request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active variant <paramref name="scope"/> currently resolves to - <c>product</c> and
    /// <c>product_variant</c> alone, the exact same resolution <see cref="StartAsync"/> itself
    /// performs before freezing <c>system_qty</c>. Exposed separately so
    /// <c>Counterpoint.Application.Inventory.StockTakeService.StartAsync</c>'s own overlap guard
    /// can resolve a candidate scope to variant ids without creating anything yet - see
    /// <see cref="ListOpenVariantSetsAsync"/>'s own remarks for what it is checked against.
    /// </summary>
    public Task<IReadOnlyList<long>> ResolveScopeAsync(
        StockTakeScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The already-resolved variant set behind every currently-<c>OPEN</c> stock take, one entry
    /// per take (<c>stock_take_line.product_variant_id</c> for lines whose parent
    /// <c>stock_take.status = 'OPEN'</c>).
    /// </summary>
    /// <remarks>
    /// What <c>StockTakeService.StartAsync</c> checks a new scope's resolved variant set against
    /// before creating it: two OPEN stock takes with non-overlapping scopes (different categories,
    /// different racks) are meant to run concurrently, but two whose scopes share a variant would
    /// each post their own correct-looking variance for it at posting time and the corrections
    /// would silently sum, over-adjusting the real balance - a gap the migration test's own comment
    /// next to <c>ux_one_open_shift</c> (deliberately not mirrored as
    /// <c>ux_one_open_stock_take</c>, because non-overlapping scopes are fine) used to explain away
    /// as "safe because variance, not an absolute quantity, is posted" alone. That protects against
    /// a sale happening *between* one stock take's freeze and its own post
    /// (<c>P2_T10_ItemsSoldDuringTheCountEndAtTheArithmeticallyCorrectFinalBalance</c>); it does not
    /// protect against two independently-started counts of the same stock, which is what this
    /// method exists to let the Application layer guard against instead of a schema constraint (no
    /// unique index can express "no overlapping variant sets" for arbitrary scope combinations).
    /// </remarks>
    public Task<IReadOnlyList<OpenStockTakeVariantSet>> ListOpenVariantSetsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one line's physical count: sets <c>counted_qty</c>, computes
    /// <c>variance = counted_qty - system_qty</c> and stamps <c>counted_at</c>. Overwrites a
    /// previous count on the same line rather than accumulating, which is what lets the same
    /// session, or a later one, correct a miscount before the batch is posted - "partial saving
    /// across sessions" (task P2-T10).
    /// </summary>
    /// <param name="countedQty">
    /// The physical count, in the product's base unit - the same unit <c>system_qty</c> was
    /// frozen in, so the two are always directly comparable with no conversion step.
    /// </param>
    /// <returns>
    /// The updated line, or null when the stock take does not exist, is not <c>OPEN</c>, or has
    /// no line for that variant - a count against a posted or abandoned stock take, or against a
    /// variant outside its scope, changes nothing.
    /// </returns>
    public Task<StockTakeLineRecord?> RecordCountAsync(
        long stockTakeId,
        long productVariantId,
        decimal countedQty,
        DateTimeOffset countedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves <c>stock_take.status</c> on, stamping <c>completed_at</c> when one is given. An
    /// ordinary <c>UPDATE</c>, the same shape as <c>SqlitePurchaseOrderStore.UpdateStatusAsync</c>
    /// - not a column-scoped trigger workaround, because <c>stock_take</c> carries no triggers.
    /// </summary>
    public Task SetStatusAsync(
        long stockTakeId,
        string status,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>What starts a stock take (<see cref="IStockTakeStore.StartAsync"/>).</summary>
/// <param name="Scope">
/// The canonical scope token (<c>Counterpoint.Domain.Inventory.StockTakeScope.ToToken</c>) -
/// already parsed and validated by the caller, never raw operator input.
/// </param>
/// <param name="StockTakeNo">
/// Allocated from <c>number_sequence</c> (<c>doc_type = 'STOCK_TAKE'</c>) by the caller, inside
/// the same transaction (CLAUDE.md invariant 4) - this port only ever stores the number handed to
/// it, the same split <see cref="IGoodsReceiptStore.CreateAsync"/> keeps for <c>grn_no</c>.
/// </param>
/// <param name="UserId">Who started the count.</param>
/// <param name="StartedAt">When the count sheet was generated - what every line's freeze reflects.</param>
public sealed record NewStockTake(string Scope, string StockTakeNo, long UserId, DateTimeOffset StartedAt);

/// <summary>
/// One currently-<c>OPEN</c> stock take's already-resolved variant set
/// (<see cref="IStockTakeStore.ListOpenVariantSetsAsync"/>).
/// </summary>
/// <param name="Id"><c>stock_take.id</c>.</param>
/// <param name="StockTakeNo">What <c>StockTakeService.StartAsync</c> names in its refusal message.</param>
/// <param name="VariantIds">Every distinct <c>product_variant_id</c> this open take's lines cover.</param>
public sealed record OpenStockTakeVariantSet(long Id, string StockTakeNo, IReadOnlyList<long> VariantIds);

/// <summary>One row of the stock take list screen.</summary>
public sealed record StockTakeSummaryRecord(
    long Id,
    string StockTakeNo,
    string Scope,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int LineCount);

/// <summary>One stock take with its lines.</summary>
public sealed record StockTakeRecord(
    long Id,
    string StockTakeNo,
    string Scope,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long UserId,
    IReadOnlyList<StockTakeLineRecord> Lines);

/// <summary>One row of <c>stock_take_line</c>, joined to what a count sheet or a variance report needs.</summary>
/// <param name="SystemQty">Frozen at generation - never re-read after <see cref="IStockTakeStore.StartAsync"/>.</param>
/// <param name="CountedQty">Null until <see cref="IStockTakeStore.RecordCountAsync"/> is called for this line.</param>
/// <param name="Variance">
/// <c>CountedQty - SystemQty</c>, null exactly when <see cref="CountedQty"/> is - what
/// <c>StockTakeService.PostAsync</c> posts through <see cref="IStockLedger"/>, never
/// <see cref="CountedQty"/> itself (task P2-T10's own named risk).
/// </param>
public sealed record StockTakeLineRecord(
    long Id,
    long ProductVariantId,
    string Sku,
    string ProductName,
    long BaseUomId,
    string UomSymbol,
    Quantity SystemQty,
    Quantity? CountedQty,
    Quantity? Variance,
    DateTimeOffset? CountedAt);
