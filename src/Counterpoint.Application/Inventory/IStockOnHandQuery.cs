using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The stock-on-hand list (task P3-T06 "Do this" #7, SRS §9 RPT-08): current quantity per variant in
/// base and alternate units, with location and reorder level.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not owner-only - SRS §9 lists RPT-08 for both roles - and cost-free at the projection.</b> There is
/// no cost column anywhere in the DTO or in this query's SQL, so a cashier session is handed nothing to
/// leak (CLAUDE.md invariant 8, AC-17). The valuation, which has cost, is
/// <see cref="IStockValuationQuery"/> and owner-only.
/// </para>
/// <para>
/// It reads the <c>stock_balance</c> projection, never the ledger (CLAUDE.md invariant 3), for active
/// variants of active, stock-tracked products (STANDARD and DECIMAL types). A variant that has never
/// moved is listed with zero.
/// </para>
/// </remarks>
public interface IStockOnHandQuery
{
    /// <summary>The stock position for every variant matching <paramref name="filter"/>, by product name then SKU.</summary>
    public Task<StockOnHandReport> GetStockOnHandAsync(
        StockOnHandFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>What the stock-on-hand list narrows to (all optional).</summary>
/// <param name="CategoryId">Products filed under this category or one of its children.</param>
/// <param name="BrandId">Products of this brand.</param>
/// <param name="SupplierId">Products linked to this supplier (<c>product_supplier</c>).</param>
/// <param name="Location">Products whose rack/bin location contains this text, case-insensitively.</param>
/// <param name="InStockOnly">When true, hides variants with zero or negative stock.</param>
public sealed record StockOnHandFilter(
    long? CategoryId = null,
    long? BrandId = null,
    long? SupplierId = null,
    string? Location = null,
    bool InStockOnly = false);

/// <summary>A variant's quantity expressed in one of the product's other units.</summary>
/// <param name="Symbol">The unit's symbol.</param>
/// <param name="Quantity">The base quantity divided by the unit's conversion factor; exact, unrounded.</param>
public sealed record StockOnHandAlternateUnit(string Symbol, decimal Quantity);

/// <summary>One variant's stock position.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="Description">The product's name.</param>
/// <param name="CategoryName">Category as filed now; empty when unfiled.</param>
/// <param name="BrandName">Brand; empty when none.</param>
/// <param name="Location">Rack or bin; empty when none.</param>
/// <param name="QtyOnHandBase">Current stock in the base unit. May be negative (Q-11).</param>
/// <param name="BaseUomSymbol">The base unit's symbol.</param>
/// <param name="AlternateUnits">The same quantity in each other unit the product sells in.</param>
/// <param name="ReorderLevel">The product's reorder level in the base unit; zero means not tracked.</param>
public sealed record StockOnHandLine(
    long ProductVariantId,
    string Sku,
    string Description,
    string CategoryName,
    string BrandName,
    string Location,
    Quantity QtyOnHandBase,
    string BaseUomSymbol,
    IReadOnlyList<StockOnHandAlternateUnit> AlternateUnits,
    Quantity ReorderLevel);

/// <summary>The stock-on-hand list.</summary>
/// <param name="Lines">The matching variants.</param>
public sealed record StockOnHandReport(IReadOnlyList<StockOnHandLine> Lines);
