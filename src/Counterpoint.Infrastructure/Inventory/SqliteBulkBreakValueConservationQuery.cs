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
/// ×100 000 000; the <c>HAVING</c> clause compares that raw product to zero directly - exact
/// integer arithmetic, no rounding, so a group is excluded only when it is genuinely balanced to
/// the last unit the database can represent. <see cref="UnbalancedBulkBreak.NetValue"/> divides
/// the raw sum back down to <see cref="Money"/>'s own ×10 000 scale for anyone reading the result -
/// a diagnostic figure, since a row only reaches this query at all because it is not zero.
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
        HAVING SUM(qty_base * unit_cost) <> 0
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
