using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Reporting.Shifts;

/// <summary>
/// The tender-by-type SQL for one shift, written once and shared by the X/Z report figures
/// (<c>XReportFiguresReader</c>) and the tender reconciliation report (task P3-T06), so the Z report a
/// shift closed with and the reconciliation that ties it out cannot disagree.
/// </summary>
/// <remarks>
/// One <c>UNION ALL</c> over the shift's own sale payments and its own return payments, tagged by source
/// and grouped by <c>tender_type</c>. A refund's <c>payment.amount</c> is stored negative
/// (docs/01_DATA_MODEL.md section 5), so it is negated back to a positive magnitude. Only completed
/// sales count: a cancelled sale never traded.
/// </remarks>
internal static class ShiftTenderBreakdown
{
    /// <summary>Takes <c>@ShiftId</c>; one row per tender type the shift used, by name.</summary>
    internal const string Sql =
        """
        SELECT tender_type AS TenderType,
               COALESCE(SUM(CASE WHEN source = 'SALE' THEN amount ELSE 0 END), 0) AS SalesAmountScaled,
               COALESCE(SUM(CASE WHEN source = 'RETURN' THEN -amount ELSE 0 END), 0) AS RefundsAmountScaled
          FROM (
                SELECT p.tender_type AS tender_type, p.amount AS amount, 'SALE' AS source
                  FROM payment p
                  JOIN sale sa ON sa.id = p.sale_id
                 WHERE sa.shift_id = @ShiftId
                   AND sa.status = 'COMPLETED'
                UNION ALL
                SELECT p.tender_type AS tender_type, p.amount AS amount, 'RETURN' AS source
                  FROM payment p
                  JOIN sale_return sr ON sr.id = p.sale_return_id
                 WHERE sr.shift_id = @ShiftId
               ) combined
         GROUP BY tender_type
         ORDER BY tender_type;
        """;

    internal static XReportTenderLine ToTenderLine(TenderRow row) => new(
        row.TenderType,
        Money.FromScaled(row.SalesAmountScaled),
        Money.FromScaled(row.RefundsAmountScaled),
        Money.FromScaled(row.SalesAmountScaled - row.RefundsAmountScaled));

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    internal sealed class TenderRow
    {
        public string TenderType { get; set; } = string.Empty;

        public long SalesAmountScaled { get; set; }

        public long RefundsAmountScaled { get; set; }
    }
}
