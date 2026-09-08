using System;
using System.Collections.Generic;
using System.Globalization;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// The catalogue-level facts a sale, a receipt or a report needs to convert a quantity between
/// units and to resolve what a unit sells for (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.1-FR-2.8).
/// </summary>
/// <remarks>
/// <para>
/// Assembled by the caller from persisted rows - <c>Counterpoint.Domain</c> does no I/O - so this
/// is a read-side view of one <c>product</c> row and its <c>product_uom</c> rows, not an EF entity
/// and not the thing a product editor screen saves back. Saving happens through the Application
/// layer's product maintenance service; this is what <see cref="UomConverter"/> and
/// <see cref="VariantMatrixGenerator"/>'s callers convert and price through.
/// </para>
/// <para>
/// <b>Exactly one base unit, enforced here as well as by the database trigger.</b>
/// <c>trg_product_uom_base_factor_insert</c>/<c>_update</c> and <c>ux_product_uom_one_base</c>
/// (§8) are the backstop a row written outside this constructor cannot go round; this constructor
/// is what stops a caller building a <see cref="Product"/> that violates the rule in memory in the
/// first place, before either database trigger has a chance to see it.
/// </para>
/// </remarks>
public sealed class Product
{
    private readonly Dictionary<long, ProductUomOption> _uomOptionsByUomId;

    /// <exception cref="ArgumentException">
    /// <paramref name="uomOptions"/> is empty, has more than one <see cref="ProductUomOption.IsBase"/>
    /// row, has none, has a base row whose unit or conversion factor does not match
    /// <paramref name="baseUomId"/>, or repeats a <see cref="ProductUomOption.UomId"/>.
    /// </exception>
    public Product(long id, string code, string name, ProductType type, long baseUomId, IReadOnlyList<ProductUomOption> uomOptions)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(uomOptions);

        Id = id;
        Code = code;
        Name = name;
        Type = type;
        BaseUomId = baseUomId;

        _uomOptionsByUomId = BuildAndValidate(uomOptions, baseUomId);
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; }

    public ProductType Type { get; }

    /// <summary>The <c>uom.id</c> stock is always held in (<c>product.base_uom_id</c>).</summary>
    public long BaseUomId { get; }

    /// <summary>Every unit this product may be sold in, base unit included.</summary>
    public IReadOnlyCollection<ProductUomOption> UomOptions => _uomOptionsByUomId.Values;

    /// <summary>The product's one base-unit option.</summary>
    public ProductUomOption BaseUomOption => _uomOptionsByUomId[BaseUomId];

    /// <summary>True for <see cref="ProductType.Service"/> and <see cref="ProductType.NonInventory"/> (FR-2.1-FR-2.8).</summary>
    public bool PostsNoStockMovement => ProductTypes.PostsNoStockMovement(Type);

    /// <summary>The option this product sells <paramref name="uomId"/> in, or null.</summary>
    public ProductUomOption? FindUom(long uomId) =>
        _uomOptionsByUomId.TryGetValue(uomId, out var option) ? option : null;

    /// <exception cref="InvalidOperationException">This product does not sell in <paramref name="uomId"/>.</exception>
    public ProductUomOption RequireUom(long uomId) =>
        FindUom(uomId) ?? throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"'{Name}' is not sold in unit {uomId}. Add that unit to the product first."));

    private static Dictionary<long, ProductUomOption> BuildAndValidate(
        IReadOnlyList<ProductUomOption> uomOptions,
        long baseUomId)
    {
        if (uomOptions.Count == 0)
        {
            throw new ArgumentException(
                "A product must sell in at least one unit - its base unit, at minimum "
                + "(docs/01_DATA_MODEL.md §8, \"at least one\" half of the base-unit guard).",
                nameof(uomOptions));
        }

        var byUomId = new Dictionary<long, ProductUomOption>();
        var baseOptions = new List<ProductUomOption>();

        foreach (var option in uomOptions)
        {
            if (!byUomId.TryAdd(option.UomId, option))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Unit {option.UomId} is listed more than once."),
                    nameof(uomOptions));
            }

            if (option.IsBase)
            {
                baseOptions.Add(option);
            }
        }

        if (baseOptions.Count != 1)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A product must have exactly one base unit; {baseOptions.Count} were marked is_base (ux_product_uom_one_base, docs/01_DATA_MODEL.md §8)."),
                nameof(uomOptions));
        }

        var baseOption = baseOptions[0];

        if (baseOption.UomId != baseUomId)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The base product_uom row is for unit {baseOption.UomId}, but product.base_uom_id is {baseUomId}. They must agree."),
                nameof(uomOptions));
        }

        if (!baseOption.Conversion.IsBase)
        {
            throw new ArgumentException(
                "The base unit's conversion factor must be exactly 1 "
                + "(trg_product_uom_base_factor_insert/_update, docs/01_DATA_MODEL.md §8).",
                nameof(uomOptions));
        }

        return byUomId;
    }
}
