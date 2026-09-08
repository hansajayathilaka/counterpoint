using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="IProductMaintenance"/> needs to create or edit a variant (SRS FR-2.6).</summary>
/// <param name="Sku">The variant's unique SKU.</param>
/// <param name="Attributes">
/// The attributes that make this SKU distinct from its siblings, for example
/// <c>{"length":"50mm","thread":"M8"}</c>. Empty for a product with no variation of its own -
/// every product has at least one variant, even an attribute-less one (docs/01_DATA_MODEL.md §3).
/// </param>
/// <param name="Price">Retail price, per base unit (<c>product_variant.price</c>).</param>
/// <param name="ConfirmBelowCost">
/// True to save <paramref name="Price"/> even though it is at or below the product's cost (SRS
/// FR-2.18). False - the default - is what the first attempt at any price should send; a caller
/// that receives <see cref="PriceBelowCostWarningException"/> shows the shop the comparison and
/// resubmits the same command with this set to proceed. The same pattern as
/// <see cref="SaveProductCommand.ConfirmDuplicate"/>.
/// </param>
/// <param name="Reason">
/// Why the price is changing, for <c>price_change_log.reason</c> (SRS FR-2.17). Optional -
/// applied only when this call actually changes an existing variant's price; a new variant has
/// no "old price" to log a change from.
/// </param>
public sealed record SaveProductVariantCommand(
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    Money Price,
    bool ConfirmBelowCost = false,
    string? Reason = null);
