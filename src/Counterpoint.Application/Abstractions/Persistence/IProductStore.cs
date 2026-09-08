using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>product</c>, <c>product_variant</c> and <c>product_uom</c>
/// (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
/// <remarks>
/// One port over three tables, not three, because the rule that binds them - a product cannot
/// exist without its base <c>product_uom</c> row, and a variant always belongs to exactly one
/// product - only holds if the writes that create them are never separated across ports the
/// caller could call out of order.
/// </remarks>
public interface IProductStore
{
    public Task<IReadOnlyList<ProductSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<ProductRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active product's name and brand - the FR-2.24 duplicate-on-creation check's search
    /// space. Deliberately its own lightweight query rather than a re-use of <see cref="ListAsync"/>:
    /// the duplicate check runs on every product creation, not once when a screen opens, and has
    /// no use for <see cref="ListAsync"/>'s unit symbol or variant count.
    /// </summary>
    public Task<IReadOnlyList<ProductDuplicateCandidate>> ListForDuplicateCheckAsync(CancellationToken cancellationToken = default);

    public Task<bool> ExistsWithCodeAsync(string code, long? excludingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the product and its base <c>product_uom</c> row (<c>is_base = 1</c>,
    /// <c>conversion_factor = 10000</c>) together, in the caller's transaction - so a product can
    /// never exist without one (docs/01_DATA_MODEL.md §8, "at least one" half of the base-unit
    /// guard).
    /// </summary>
    public Task<long> CreateAsync(SaveProductCommand command, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, SaveProductCommand command, CancellationToken cancellationToken = default);

    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<ProductVariantRecord>> ListVariantsAsync(long productId, CancellationToken cancellationToken = default);

    public Task<ProductVariantRecord?> FindVariantByIdAsync(long variantId, CancellationToken cancellationToken = default);

    public Task<bool> ExistsWithSkuAsync(string sku, long? excludingId, CancellationToken cancellationToken = default);

    public Task<long> CreateVariantAsync(long productId, SaveProductVariantCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts every variant in one operation, in the caller's transaction - the matrix
    /// generator's commit step (SRS FR-2.6). Returns the new variant ids in the order given.
    /// </summary>
    public Task<IReadOnlyList<long>> CreateVariantsAsync(
        long productId,
        IReadOnlyList<SaveProductVariantCommand> commands,
        CancellationToken cancellationToken = default);

    public Task UpdateVariantAsync(long variantId, SaveProductVariantCommand command, CancellationToken cancellationToken = default);

    public Task SetVariantActiveAsync(long variantId, bool active, CancellationToken cancellationToken = default);

    /// <summary>Every unit this product sells in, base unit included.</summary>
    public Task<IReadOnlyList<ProductUomRecord>> ListUomOptionsAsync(long productId, CancellationToken cancellationToken = default);

    public Task<ProductUomRecord?> FindUomOptionByIdAsync(long uomOptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a non-base unit the product may also be sold in. The base unit is created with the
    /// product itself (<see cref="CreateAsync"/>) and is never added through here.
    /// </summary>
    public Task<long> AddUomOptionAsync(long productId, SaveProductUomCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The row is the product's base unit.</exception>
    public Task UpdateUomOptionAsync(long uomOptionId, SaveProductUomCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The row is the product's base unit.</exception>
    /// <returns>False when the database's own foreign keys refused the delete.</returns>
    public Task<bool> RemoveUomOptionAsync(long uomOptionId, CancellationToken cancellationToken = default);
}
