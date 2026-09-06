using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// What the catalogue knows about one sellable variant, as the sale path needs it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an internal read model, not a cashier DTO.</b> It carries
/// <see cref="UnitCost"/>, and cost never reaches a cashier's screen (CLAUDE.md invariant 8,
/// SRS NFR-S2, AC-17). The type the UI receives is
/// <c>Counterpoint.Application.Sales.ScannedItem</c>, which has no cost field at all - so
/// there is nothing there to leak.
/// </para>
/// <para>
/// Prices and costs are snapshotted onto <c>sale_line</c> at completion, because the
/// catalogue moves and a return six months later must still refund what was paid
/// (CLAUDE.md invariant 10).
/// </para>
/// </remarks>
/// <param name="ProductVariantId">The variant being sold.</param>
/// <param name="Description">The product name, as it will be snapshotted onto the bill line.</param>
/// <param name="BaseUomId">The product's base unit. Everything in the ledger is in base units.</param>
/// <param name="UomSymbol">The base unit's symbol, for the receipt.</param>
/// <param name="UnitPrice">Retail price per base unit.</param>
/// <param name="UnitCost">Moving-average cost per base unit. Owner-only information.</param>
/// <param name="TaxRate">The rate on the product's tax class.</param>
public sealed record CatalogueItem(
    long ProductVariantId,
    string Description,
    long BaseUomId,
    string UomSymbol,
    Money UnitPrice,
    Money UnitCost,
    TaxRate TaxRate);
