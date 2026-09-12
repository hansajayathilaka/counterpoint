using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Reads and writes <c>goods_receipt</c> and <c>goods_receipt_line</c> (docs/01_DATA_MODEL.md §4, SRS FR-4.7, FR-4.8, AC-08).</summary>
/// <remarks>
/// Neither table is append-only or hash-chained (CLAUDE.md invariant 5 names exactly which
/// tables are; these two are not among them) - unlike a <c>sale</c>, a goods receipt has no
/// column any trigger protects. The stock and cost effects of a receipt go through
/// <see cref="IStockLedger.PostAsync"/> and <see cref="IProductSupplierStore"/> instead, in the
/// same transaction as <see cref="CreateAsync"/>; nothing here posts a stock movement itself.
/// </remarks>
public interface IGoodsReceiptStore
{
    /// <summary>Every goods receipt, newest first - a list screen's whole read side.</summary>
    public Task<IReadOnlyList<GoodsReceiptSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One goods receipt with its lines, or null when it does not exist.</summary>
    public Task<GoodsReceiptRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the receipt and every one of its lines together, in the caller's transaction - so
    /// a receipt can never exist with no lines (docs/01_DATA_MODEL.md §4).
    /// </summary>
    public Task<long> CreateAsync(NewGoodsReceipt receipt, CancellationToken cancellationToken = default);
}
