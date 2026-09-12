using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>product_supplier</c> (docs/01_DATA_MODEL.md §3) - the link between a
/// product and one of its suppliers, and what that supplier last charged for it.
/// </summary>
/// <remarks>
/// <see cref="ISupplierStore.HasLinksAsync"/> already anticipated this table being written by a
/// goods receipt (P2-T06's own remark: "the Phase 2 tables already exist even though nothing
/// writes to them yet"). <see cref="UpsertLastCostAsync"/> is that write.
/// </remarks>
public interface IProductSupplierStore
{
    /// <summary>
    /// Records <paramref name="lastCost"/> as this supplier's most recent price for this product,
    /// per base unit, deliberately excluding any one receipt's own freight apportionment - it is
    /// a comparison figure between suppliers and over time, not the landed cost a specific
    /// shipment carried (that lives on <c>goods_receipt_line.unit_cost_base</c> and feeds the
    /// stock ledger's moving average instead, SRS FR-4.4).
    /// </summary>
    /// <param name="productId">The product, not the variant - <c>product_supplier</c> is keyed by product.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="lastCost">The supplier's per-base-unit price on this receipt.</param>
    /// <remarks>
    /// Inserts the <c>product_supplier</c> row if this product and supplier have never been
    /// linked before (the pair is unique, docs/01_DATA_MODEL.md §3's <c>UNIQUE (product_id,
    /// supplier_id)</c>), or updates <c>last_cost</c> on the existing one otherwise.
    /// </remarks>
    public Task UpsertLastCostAsync(
        long productId,
        long supplierId,
        Money lastCost,
        CancellationToken cancellationToken = default);
}
