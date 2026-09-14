using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// Answers <see cref="IBulkBreakValueConservationQuery"/> off a read connection (task P2-T09 "Do
/// this" #4).
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over a read connection, not EF - a report query on the hot read path, the
/// same split every other <c>Sqlite*Query</c> reader in this folder draws
/// (<c>SqliteAdjustmentHistoryQuery</c>'s own remarks). It groups <c>stock_movement</c> by
/// <c>ref_doc_id</c> for <c>ref_doc_type = 'BULK_BREAK'</c>, which <c>ix_movement_ref</c>
/// (<c>(ref_doc_type, ref_doc_id)</c>) already covers - no new index is needed.
/// </para>
/// <para>
/// <b>Scale.</b> <c>qty_base</c> and <c>unit_cost</c> are each stored scaled ×10 000
/// (<see cref="Quantity"/>, <see cref="Money"/>). Their raw SQL product is therefore scaled
/// ×100 000 000. <see cref="UnbalancedBulkBreak.NetValue"/> divides the raw sum back down to
/// <see cref="Money"/>'s own ×10 000 scale for anyone reading the result - a diagnostic figure of
/// the actual (small) net value for a flagged group, unchanged by the tolerance below, which only
/// decides whether a group is reported at all.
/// </para>
/// <para>
/// <b>The <c>HAVING</c> clause is a bounded-tolerance comparison, not an exact one - and that is a
/// correction, not the original design.</b> This query originally compared the raw sum to zero
/// exactly ("a group is excluded only when it is genuinely balanced to the last unit the database
/// can represent"). That was wrong: <c>PostBulkBreakHandler</c>'s own remarks on
/// <c>unitCostIn</c> ("the one division this cannot avoid") already document that the destination
/// leg's unit cost is an ordinary decimal division, quantised to <see cref="Money"/>'s four decimal
/// places exactly once, when <c>SqliteStockLedger.PostAsync</c> writes it - and that whenever
/// <see cref="BulkBreakCommand.ActualQuantity"/> does not evenly divide the combined
/// source-plus-wastage value (the ordinary case for quantities a shop actually types, not a rare
/// one), that single quantisation leaves a residual, bounded by "half of
/// <see cref="Money.MoneyScale"/>'s smallest unit, multiplied by
/// <see cref="BulkBreakCommand.ActualQuantity"/>". An exact-zero comparison flags that expected,
/// bounded residual on nearly every real break - a false positive on the report's own main
/// scenario, not a real value leak (proven empirically: 1 000 random breaks in
/// <c>BulkBreakTests</c> produce a nonzero residual on every one whose actual quantity does not
/// happen to divide the combined value evenly).
/// </para>
/// <para>
/// <b>Deriving the tolerance, in the query's own raw ×100 000 000 scale.</b> Let <c>s</c> = 10 000
/// (<see cref="Money.MoneyScale"/> and <see cref="Quantity.QtyScale"/> - the same constant,
/// <c>ScaledDecimal.Scale</c>) and <c>q</c> = <see cref="BulkBreakCommand.ActualQuantity"/> in real
/// units. The handler's own documented bound on the residual, in real currency, is
/// <c>(0.5 / s) * q</c> (half the smallest <see cref="Money"/> unit, times the quantity that
/// residual is spread over). <see cref="UnbalancedBulkBreak.NetValue"/>'s raw sum is scaled ×s²
/// (money scale × quantity scale), so that same bound in the raw scale is
/// <c>(0.5 / s) * q * s² = 0.5 * q * s</c>. But <c>q * s</c> is exactly the stored, scaled
/// <c>qty_base</c> of the group's own <c>BULK_BREAK_IN</c> row (<see cref="Quantity.ToScaled"/> on
/// <see cref="BulkBreakCommand.ActualQuantity"/>) - not an assumption, the literal value already
/// sitting in that row. So the bound, entirely in terms of columns this query already reads, is
/// <c>0.5 * ABS(qty_base of the BULK_BREAK_IN row)</c>. Multiplying both sides by 2 keeps the
/// comparison in exact 64-bit integers (no SQLite floating point): a group is flagged only when
/// <c>2 * ABS(SUM(qty_base * unit_cost)) &gt; MAX(qty_base of its BULK_BREAK_IN row's ABS)</c>. A
/// group whose <c>BULK_BREAK_IN</c> row is missing entirely (a real defect - a missing leg) has no
/// row to take that bound from, so <c>MAX(...)</c> is 0 and the comparison degrades to the
/// original exact-zero check for that group - appropriately, since a missing leg is not rounding
/// noise. This bound is exact, not padded "for safety": it is the same ceiling
/// <c>PostBulkBreakHandler</c>'s own remarks already document and the generative test in
/// <c>BulkBreakTests</c> already measures (worst case ≈ Rs 0.0122 across 1 000 random breaks with
/// quantities up to 300, against a documented ceiling of ≈ Rs 0.015 for that scale) - and it is
/// three to six orders of magnitude tighter than what a real defect produces: a missing or
/// mis-valued leg is a whole movement's value, typically tens to thousands of rupees, not a
/// fraction of a currency sub-unit, so no real defect can hide inside this tolerance.
/// </para>
/// </remarks>
internal sealed class SqliteBulkBreakValueConservationQuery : IBulkBreakValueConservationQuery
{
    private const string Sql =
        """
        SELECT ref_doc_id AS BulkBreakId,
               SUM(qty_base * unit_cost) AS NetValueRaw
          FROM stock_movement
         WHERE ref_doc_type = 'BULK_BREAK'
         GROUP BY ref_doc_id
        HAVING 2 * ABS(SUM(qty_base * unit_cost))
               > MAX(CASE WHEN movement_type = 'BULK_BREAK_IN' THEN ABS(qty_base) ELSE 0 END)
         ORDER BY ref_doc_id;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteBulkBreakValueConservationQuery(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnbalancedBulkBreak>> FindUnbalancedAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<UnbalancedBulkBreak> result =
                [.. rows.Select(row => new UnbalancedBulkBreak(row.BulkBreakId, Money.FromScaled(row.NetValueRaw / 10_000L)))];
            return result;
        }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    private sealed class Row
    {
        public long BulkBreakId { get; set; }

        public long NetValueRaw { get; set; }
    }
}
