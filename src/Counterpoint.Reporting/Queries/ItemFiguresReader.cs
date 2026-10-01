using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// Net sales, quantity and (on request) COGS per product variant, category or brand, from
/// <c>sale_line</c> and <c>sale_return_line</c> (task P3-T05: RPT-02 and RPT-03 by item/category/brand).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why line level, in C#.</b> A line's net is <c>line_total</c> less its share of the bill
/// discount, and that share is a per-bill allocation (<c>BillDiscountSplit</c>, the same split a
/// receipt and a return use) that SQL cannot reproduce exactly; COGS is <c>unit_cost x qty_base</c>,
/// a product SQL must not compute (CLAUDE.md invariant 1). So lines are read whole and folded in C#.
/// This is the raw-tables opt-in: <c>daily_product_summary.net</c> does not subtract the bill
/// discount, so the rollup cannot serve this report (<c>docs/report-definitions.md</c> section 3).
/// </para>
/// <para>
/// <b>Reconciles to canonical net.</b> Per row: <c>sum(line_total - bill discount share) - sum(line_refund
/// of returns dated in the range)</c>. Across rows the lines sum to <c>sale.subtotal</c>, the shares to
/// <c>sale.bill_discount</c> (the allocator sums exactly) and the refunds to <c>sale_return.subtotal</c>,
/// which is exactly <see cref="CanonicalFigures.Net"/>'s <c>subtotal - bill_discount - return_subtotal</c>.
/// </para>
/// <para>
/// <b>COGS is the snapshot</b> - <c>sale_line.unit_cost</c> and <c>sale_return_line.unit_cost</c>, never
/// the product's current cost (CLAUDE.md invariant 10); return cost comes back only for SELLABLE
/// lines, as the canonical definition says. With <c>includeCost</c> off, <c>unit_cost</c> is not read.
/// </para>
/// </remarks>
internal sealed class ItemFiguresReader
{
    /// <summary>The bucket label for sale lines with no product (open items), on the Item dimension.</summary>
    internal const string OpenItemsName = "(Open items)";

    /// <summary>The bucket label for lines whose product has no category.</summary>
    internal const string NoCategoryName = "(No category)";

    /// <summary>The bucket label for lines whose product has no brand.</summary>
    internal const string NoBrandName = "(No brand)";

    // Row ids start at 1, so 0 safely stands for "no key" (open item, unfiled).
    private const long NoKey = 0;

    private const string SaleLinesSql =
        """
        SELECT sl.sale_id AS SaleId, sa.bill_discount AS BillDiscountScaled,
               sl.qty AS QtyScaled, sl.uom_id AS UomId, sl.unit_price AS UnitPriceScaled,
               sl.discount AS DiscountScaled, sl.line_total AS LineTotalScaled, sl.qty_base AS QtyBaseScaled,
               CASE WHEN @IncludeCost = 1 THEN sl.unit_cost ELSE 0 END AS UnitCostScaled,
               sl.product_variant_id AS VariantId, pv.sku AS Sku, p.name AS ProductName,
               p.category_id AS CategoryId, p.brand_id AS BrandId,
               p.base_uom_id AS BaseUomId, bu.symbol AS BaseUomSymbol
          FROM sale_line sl
          JOIN sale sa ON sa.id = sl.sale_id
          LEFT JOIN product_variant pv ON pv.id = sl.product_variant_id
          LEFT JOIN product p ON p.id = pv.product_id
          LEFT JOIN uom bu ON bu.id = p.base_uom_id
         WHERE sa.business_date >= @From AND sa.business_date <= @To
           AND sa.status = 'COMPLETED'
         ORDER BY sl.sale_id, sl.line_no;
        """;

    private const string ReturnLinesSql =
        """
        SELECT srl.qty_base AS QtyBaseScaled, srl.line_refund AS LineRefundScaled,
               CASE WHEN @IncludeCost = 1 THEN srl.unit_cost ELSE 0 END AS UnitCostScaled,
               srl.disposition AS Disposition,
               srl.product_variant_id AS VariantId, pv.sku AS Sku, p.name AS ProductName,
               p.category_id AS CategoryId, p.brand_id AS BrandId,
               p.base_uom_id AS BaseUomId, bu.symbol AS BaseUomSymbol
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
          LEFT JOIN product_variant pv ON pv.id = srl.product_variant_id
          LEFT JOIN product p ON p.id = pv.product_id
          LEFT JOIN uom bu ON bu.id = p.base_uom_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To;
        """;

    private const string CategoriesSql = "SELECT id AS Id, name AS Name, parent_id AS ParentId FROM category;";

    private const string BrandsSql = "SELECT id AS Id, name AS Name FROM brand;";

    private readonly IReportConnectionFactory _connectionFactory;

    internal ItemFiguresReader(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>One entry per group, in no particular order; the caller ranks them.</summary>
    internal async Task<IReadOnlyList<ItemFigures>> ReadAsync(
        ReportDateRange range,
        SalesBreakdownDimension dimension,
        bool includeCost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new
        {
            From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IncludeCost = includeCost ? 1 : 0,
        };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var saleLines = (await connection.QueryAsync<SaleLineRow>(
                new CommandDefinition(SaleLinesSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var returnLines = await connection.QueryAsync<ReturnLineRow>(
                new CommandDefinition(ReturnLinesSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var names = await ReadNamesAsync(connection, dimension, cancellationToken).ConfigureAwait(false);

            var groups = new Dictionary<long, Group>();

            foreach (var bill in saleLines.GroupBy(line => line.SaleId))
            {
                var lines = bill.ToList();
                var shares = BillDiscountSplit.Allocate(
                    Money.FromScaled(lines[0].BillDiscountScaled),
                    [
                        .. lines.Select(line => BillDiscountSplit.Weight(
                            Money.FromScaled(line.UnitPriceScaled),
                            Quantity.FromScaled(line.QtyScaled, line.UomId),
                            Money.FromScaled(line.DiscountScaled))),
                    ]);

                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var group = GroupFor(groups, dimension, names, line);

                    group.QtyScaled += line.QtyBaseScaled;
                    group.Net += Money.FromScaled(line.LineTotalScaled) - shares[i];

                    if (includeCost)
                    {
                        group.Cogs += CanonicalFigures.LineCogs(line.UnitCostScaled, line.QtyBaseScaled);
                    }
                }
            }

            foreach (var line in returnLines)
            {
                var group = GroupFor(groups, dimension, names, line);

                group.QtyScaled -= line.QtyBaseScaled;
                group.Net -= Money.FromScaled(line.LineRefundScaled);

                // Only a SELLABLE line goes back on the shelf, so only its cost is recovered.
                if (includeCost && string.Equals(line.Disposition, "SELLABLE", StringComparison.Ordinal))
                {
                    group.Cogs -= CanonicalFigures.LineCogs(line.UnitCostScaled, line.QtyBaseScaled);
                }
            }

            return
            [
                .. groups.Values.Select(group => new ItemFigures(
                    group.Key == NoKey ? null : group.Key,
                    group.Code,
                    group.Name,
                    Quantity.FromScaled(group.QtyScaled, group.UomId),
                    group.UomSymbol,
                    group.Net,
                    group.Cogs)),
            ];
        }
    }

    private static Group GroupFor(
        Dictionary<long, Group> groups,
        SalesBreakdownDimension dimension,
        DimensionNames names,
        IProductColumns line)
    {
        long key;
        string code = string.Empty;
        string name;
        long uomId = 0;
        string? uomSymbol = null;

        switch (dimension)
        {
            case SalesBreakdownDimension.Item:
                key = line.VariantId ?? NoKey;
                if (line.VariantId is null)
                {
                    name = OpenItemsName;
                }
                else
                {
                    code = line.Sku ?? string.Empty;
                    name = line.ProductName ?? code;
                    uomId = line.BaseUomId ?? 0;
                    uomSymbol = line.BaseUomSymbol;
                }

                break;

            case SalesBreakdownDimension.Category:
                key = line.CategoryId ?? NoKey;
                name = line.CategoryId is { } categoryId && names.Categories.TryGetValue(categoryId, out var category)
                    ? category
                    : NoCategoryName;
                break;

            case SalesBreakdownDimension.Brand:
                key = line.BrandId ?? NoKey;
                name = line.BrandId is { } brandId && names.Brands.TryGetValue(brandId, out var brand)
                    ? brand
                    : NoBrandName;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unknown breakdown dimension.");
        }

        if (!groups.TryGetValue(key, out var group))
        {
            group = new Group(key, code, name, uomId, uomSymbol);
            groups[key] = group;
        }

        return group;
    }

    private static async Task<DimensionNames> ReadNamesAsync(
        System.Data.Common.DbConnection connection,
        SalesBreakdownDimension dimension,
        CancellationToken cancellationToken)
    {
        var categories = new Dictionary<long, string>();
        var brands = new Dictionary<long, string>();

        if (dimension == SalesBreakdownDimension.Category)
        {
            var rows = (await connection.QueryAsync<CategoryRow>(
                new CommandDefinition(CategoriesSql, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
            var byId = rows.ToDictionary(row => row.Id);

            foreach (var row in rows)
            {
                // Two levels only (FR-2.20): "Parent / Child" names a sub-category unambiguously.
                categories[row.Id] = row.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
                    ? parent.Name + " / " + row.Name
                    : row.Name;
            }
        }
        else if (dimension == SalesBreakdownDimension.Brand)
        {
            var rows = await connection.QueryAsync<BrandRow>(
                new CommandDefinition(BrandsSql, cancellationToken: cancellationToken)).ConfigureAwait(false);

            foreach (var row in rows)
            {
                brands[row.Id] = row.Name;
            }
        }

        return new DimensionNames(categories, brands);
    }

    private sealed record DimensionNames(Dictionary<long, string> Categories, Dictionary<long, string> Brands);

    private sealed class Group(long key, string code, string name, long uomId, string? uomSymbol)
    {
        internal long Key { get; } = key;

        internal string Code { get; } = code;

        internal string Name { get; } = name;

        internal long UomId { get; } = uomId;

        internal string? UomSymbol { get; } = uomSymbol;

        internal long QtyScaled { get; set; }

        internal Money Net { get; set; } = Money.Zero;

        internal Money Cogs { get; set; } = Money.Zero;
    }

    /// <summary>The product columns both line shapes carry, so grouping is written once.</summary>
    private interface IProductColumns
    {
        public long? VariantId { get; }

        public string? Sku { get; }

        public string? ProductName { get; }

        public long? CategoryId { get; }

        public long? BrandId { get; }

        public long? BaseUomId { get; }

        public string? BaseUomSymbol { get; }
    }

    private sealed class SaleLineRow : IProductColumns
    {
        public long SaleId { get; set; }

        public long BillDiscountScaled { get; set; }

        public long QtyScaled { get; set; }

        public long UomId { get; set; }

        public long UnitPriceScaled { get; set; }

        public long DiscountScaled { get; set; }

        public long LineTotalScaled { get; set; }

        public long QtyBaseScaled { get; set; }

        public long UnitCostScaled { get; set; }

        public long? VariantId { get; set; }

        public string? Sku { get; set; }

        public string? ProductName { get; set; }

        public long? CategoryId { get; set; }

        public long? BrandId { get; set; }

        public long? BaseUomId { get; set; }

        public string? BaseUomSymbol { get; set; }
    }

    private sealed class ReturnLineRow : IProductColumns
    {
        public long QtyBaseScaled { get; set; }

        public long LineRefundScaled { get; set; }

        public long UnitCostScaled { get; set; }

        public string Disposition { get; set; } = string.Empty;

        public long? VariantId { get; set; }

        public string? Sku { get; set; }

        public string? ProductName { get; set; }

        public long? CategoryId { get; set; }

        public long? BrandId { get; set; }

        public long? BaseUomId { get; set; }

        public string? BaseUomSymbol { get; set; }
    }

    private sealed class CategoryRow
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public long? ParentId { get; set; }
    }

    private sealed class BrandRow
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}

/// <summary>One group's net quantity, net sales and COGS (zero unless cost was asked for).</summary>
internal sealed record ItemFigures(
    long? Key,
    string Code,
    string Name,
    Quantity QtyBase,
    string? UomSymbol,
    Money Net,
    Money Cogs);
