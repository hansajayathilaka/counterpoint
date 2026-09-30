using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// The one place a sale line's taxable value is worked out, shared by the X/Z report's tax breakdown and
/// the tax report (SRS RPT-19) so the two cannot drift (task P3-T06).
/// </summary>
/// <remarks>
/// <para>
/// A line's taxable value is <c>line_total</c> (already net of tax in both pricing modes, so never
/// reduced by the line's tax) less the line's share of its bill's discount. That share is a per-bill
/// allocation SQL cannot reproduce exactly, so the rows are read line by line and the split is taken
/// here with <see cref="BillDiscountSplit"/> over the stored snapshot columns - the same split a
/// receipt, a return and the sales-by-item report use.
/// </para>
/// <para>
/// An exchange's replacement sale keeps <c>bill_discount</c> at zero unconditionally
/// (<c>ExchangeTenderType0010</c>): its credit settles as an EXCHANGE payment, never a discount. The
/// <c>CASE</c> in <see cref="SelectSql"/> is therefore defensive, not load-bearing; the derived table
/// finds those sales in one pass over <c>sale_return</c> rather than once per row.
/// </para>
/// <para>
/// Callers append their own <c>WHERE</c> (completed sales in a shift, or in a date range) and
/// <see cref="OrderSql"/>; the order matters because the bill discount is allocated across a bill's lines
/// in line order. Nothing here rounds: the split allocates exactly (CLAUDE.md invariant 2).
/// </para>
/// </remarks>
internal static class TaxableLines
{
    /// <summary>The line-level select every tax-by-rate read starts from. Append a <c>WHERE</c> on <c>sa</c>/<c>sl</c>.</summary>
    internal const string SelectSql =
        """
        SELECT sa.id AS SaleId,
               CASE WHEN ex.exchange_sale_id IS NULL THEN sa.bill_discount ELSE 0 END AS BillDiscountScaled,
               sl.qty AS QtyScaled, sl.uom_id AS UomId, sl.unit_price AS UnitPriceScaled, sl.discount AS DiscountScaled,
               sl.tax_rate AS TaxRateScaled, sl.line_total AS LineTotalScaled, sl.tax AS TaxScaled
          FROM sale_line sl
          JOIN sale sa ON sa.id = sl.sale_id
          LEFT JOIN (SELECT DISTINCT exchange_sale_id FROM sale_return WHERE exchange_sale_id IS NOT NULL) ex
            ON ex.exchange_sale_id = sa.id

        """;

    /// <summary>The order the bill discount is allocated in: bill by bill, line by line.</summary>
    internal const string OrderSql = "\n ORDER BY sa.id, sl.line_no;";

    /// <summary>The per-line allocation: taxable value and tax at the line's own stored rate.</summary>
    internal static IReadOnlyList<TaxableLine> Allocate(IReadOnlyList<TaxLineRow> rows)
    {
        var taxable = new List<TaxableLine>(rows.Count);

        foreach (var bill in rows.GroupBy(row => row.SaleId))
        {
            var lines = bill.ToList();
            var shares = BillDiscountSplit.Allocate(
                Money.FromScaled(lines[0].BillDiscountScaled),
                [.. lines.Select(line => BillDiscountSplit.Weight(
                    Money.FromScaled(line.UnitPriceScaled),
                    Quantity.FromScaled(line.QtyScaled, line.UomId),
                    Money.FromScaled(line.DiscountScaled)))]);

            for (var i = 0; i < lines.Count; i++)
            {
                taxable.Add(new TaxableLine(
                    lines[i].TaxRateScaled,
                    lines[i].LineTotalScaled - shares[i].ToScaled(),
                    lines[i].TaxScaled));
            }
        }

        return taxable;
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="SelectSql"/> onto.</summary>
    internal sealed class TaxLineRow
    {
        public long SaleId { get; set; }

        public long BillDiscountScaled { get; set; }

        public long QtyScaled { get; set; }

        public long UomId { get; set; }

        public long UnitPriceScaled { get; set; }

        public long DiscountScaled { get; set; }

        public long TaxRateScaled { get; set; }

        public long LineTotalScaled { get; set; }

        public long TaxScaled { get; set; }
    }
}

/// <summary>One sale line's taxable value and tax at its stored rate, all scaled integers.</summary>
/// <param name="RateScaled">The line's stored <c>tax_rate</c>.</param>
/// <param name="TaxableScaled"><c>line_total</c> less the line's share of the bill discount.</param>
/// <param name="TaxScaled">The line's stored <c>tax</c>.</param>
internal readonly record struct TaxableLine(long RateScaled, long TaxableScaled, long TaxScaled);
