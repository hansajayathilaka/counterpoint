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
public sealed record SaveProductVariantCommand(
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    Money Price);
