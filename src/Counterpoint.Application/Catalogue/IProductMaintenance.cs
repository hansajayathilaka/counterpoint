using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// Maintaining <c>product</c>, <c>product_variant</c> and <c>product_uom</c> - the defining
/// feature of hardware retail (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
/// Owner only, the same as every other catalogue-administration screen (SRS §3.3 ROLE-2, NFR-S2, AC-17).
/// </summary>
[RequiresRole(Role.Owner)]
public interface IProductMaintenance
{
    public Task<IReadOnlyList<ProductSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<ProductRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a product and its base unit together, in one transaction, so a product can never
    /// exist without one (docs/01_DATA_MODEL.md §8).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The code is already used, or the category, brand, base unit or tax class does not exist.
    /// </exception>
    public Task<long> CreateAsync(SaveProductCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">
    /// As <see cref="CreateAsync"/>, or the base unit differs from the product's existing one -
    /// changing it would silently redefine every quantity already on the books.
    /// </exception>
    public Task UpdateAsync(long id, SaveProductCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a product off. Always succeeds - the FR-2.1 pattern.</summary>
    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<ProductVariantRecord>> ListVariantsAsync(long productId, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The SKU is already used, or the product does not exist.</exception>
    public Task<long> CreateVariantAsync(long productId, SaveProductVariantCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The SKU is already used by a different variant.</exception>
    public Task UpdateVariantAsync(long variantId, SaveProductVariantCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a variant off. Always succeeds.</summary>
    public Task DeactivateVariantAsync(long variantId, CancellationToken cancellationToken = default);

    public Task ReactivateVariantAsync(long variantId, CancellationToken cancellationToken = default);

    /// <summary>Every unit this product sells in, base unit included.</summary>
    public Task<IReadOnlyList<ProductUomRecord>> ListUomOptionsAsync(long productId, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">
    /// The unit does not exist or is inactive, is already one of this product's units, or is this
    /// product's base unit (added automatically when the product was created).
    /// </exception>
    public Task<long> AddUomOptionAsync(long productId, SaveProductUomCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The row is the product's base unit.</exception>
    public Task UpdateUomOptionAsync(long uomOptionId, SaveProductUomCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The row is the product's base unit, or is referenced by a sale line.</exception>
    public Task RemoveUomOptionAsync(long uomOptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What generating variants for every combination of <paramref name="axes"/> would create,
    /// without writing anything - the preview before commit (SRS FR-2.6).
    /// </summary>
    public Task<VariantMatrixPreview> PreviewVariantMatrixAsync(
        long productId,
        IReadOnlyList<VariantAxis> axes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates every combination in <paramref name="combinations"/> as a new variant, in one
    /// transaction, skipping (re-checked here, not just at preview time) any combination a
    /// variant already exists for. SKUs are generated from the product's code and the
    /// combination's attribute values.
    /// </summary>
    /// <param name="defaultPrice">The price every generated variant starts at - editable afterwards.</param>
    /// <returns>The new variant ids, in the same order as <paramref name="combinations"/> once duplicates are removed.</returns>
    public Task<IReadOnlyList<long>> CommitVariantMatrixAsync(
        long productId,
        IReadOnlyList<VariantAttributes> combinations,
        Money defaultPrice,
        CancellationToken cancellationToken = default);
}
