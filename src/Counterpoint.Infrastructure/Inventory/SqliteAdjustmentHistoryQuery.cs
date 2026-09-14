using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// Answers <see cref="IAdjustmentHistoryQuery"/> off a read connection (task P2-T08 "Do this" #4).
/// </summary>
/// <remarks>
/// Hand-written SQL over a read connection, not EF - this is a report query on the hot read path
/// (CLAUDE.md "Stack"), the same split every other <c>Sqlite*Query</c> reader in this folder draws.
/// It reads <c>stock_movement</c> straight off the ledger rather than a projection, which is the
/// one place that is correct to do so (CLAUDE.md invariant 3's own "never <c>SUM</c>" rule is
/// about deriving a running balance from the ledger on a hot read path; this is a history listing
/// of the ledger's own rows, nothing is summed).
/// </remarks>
internal sealed class SqliteAdjustmentHistoryQuery : IAdjustmentHistoryQuery
{
    private const string Sql =
        """
        SELECT sm.id AS MovementId,
               sm.occurred_at AS OccurredAtText,
               sm.movement_type AS MovementType,
               sm.product_variant_id AS ProductVariantId,
               pv.sku AS Sku,
               p.name AS Description,
               p.base_uom_id AS BaseUomId,
               sm.qty_base AS QtyBaseScaled,
               sm.balance_after AS BalanceAfterScaled,
               sm.unit_cost AS UnitCostScaled,
               sm.note AS Reason,
               sm.user_id AS UserId,
               u.display_name AS UserDisplayName
          FROM stock_movement sm
          JOIN product_variant pv ON pv.id = sm.product_variant_id
          JOIN product p ON p.id = pv.product_id
          JOIN app_user u ON u.id = sm.user_id
         WHERE sm.movement_type IN ('ADJUSTMENT', 'DAMAGE')
           AND (@Type IS NULL OR sm.movement_type = @Type)
           AND (@From IS NULL OR sm.occurred_at >= @From)
           AND (@To IS NULL OR sm.occurred_at <= @To)
         ORDER BY sm.occurred_at DESC, sm.id DESC;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteAdjustmentHistoryQuery(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdjustmentHistoryLine>> ListAsync(
        AdjustmentHistoryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                Sql,
                new
                {
                    Type = filter.Type is { } type ? AdjustmentTypes.ToToken(type) : null,
                    From = filter.From?.ToString(Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture),
                    To = filter.To?.ToString(Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture),
                },
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<AdjustmentHistoryLine> result = [.. rows.Select(ToLine)];
            return result;
        }
    }

    private static AdjustmentHistoryLine ToLine(Row row) => new(
        row.MovementId,
        DateTimeOffset.ParseExact(
            row.OccurredAtText, Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture, DateTimeStyles.None),
        row.MovementType,
        row.ProductVariantId,
        row.Sku,
        row.Description,
        Quantity.FromScaled(row.QtyBaseScaled, row.BaseUomId),
        Quantity.FromScaled(row.BalanceAfterScaled, row.BaseUomId),
        Money.FromScaled(row.UnitCostScaled),
        row.Reason,
        row.UserId,
        row.UserDisplayName);

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    private sealed class Row
    {
        public long MovementId { get; set; }

        public string OccurredAtText { get; set; } = string.Empty;

        public string MovementType { get; set; } = string.Empty;

        public long ProductVariantId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public long QtyBaseScaled { get; set; }

        public long BalanceAfterScaled { get; set; }

        public long UnitCostScaled { get; set; }

        public string? Reason { get; set; }

        public long UserId { get; set; }

        public string UserDisplayName { get; set; } = string.Empty;
    }
}
