using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Cash;

/// <summary>
/// Answers <see cref="ICashMovementReader"/> off a read connection (task P3-T01 "Do this" #2, #4).
/// </summary>
/// <remarks>
/// Hand-written SQL over a read connection, not EF - the same split
/// <see cref="Counterpoint.Infrastructure.Dashboard.SqliteDashboardReader"/> and
/// <see cref="Counterpoint.Infrastructure.Inventory.SqliteAdjustmentHistoryQuery"/> draw. Cash
/// sales and cash refunds are each scoped to this shift's own rows - <c>sale.shift_id</c> for a
/// tender, <c>sale_return.shift_id</c> for a refund - never a whole-table scan of <c>payment</c>.
/// </remarks>
internal sealed class SqliteCashMovementReader : ICashMovementReader
{
    private const string CashTenderType = "CASH";

    private const string HistorySql =
        """
        SELECT cm.id AS Id,
               cm.shift_id AS ShiftId,
               cm.direction AS Direction,
               cm.amount AS AmountScaled,
               cm.reason AS Reason,
               cm.user_id AS UserId,
               u.display_name AS UserDisplayName,
               cm.occurred_at AS OccurredAtText
          FROM cash_movement cm
          JOIN app_user u ON u.id = cm.user_id
         WHERE cm.shift_id = @ShiftId
         ORDER BY cm.occurred_at DESC, cm.id DESC;
        """;

    private const string OpeningFloatSql =
        """
        SELECT opening_float FROM shift WHERE id = @ShiftId;
        """;

    private const string CashSalesSql =
        """
        SELECT COALESCE(SUM(p.amount), 0)
          FROM payment p
          JOIN sale sa ON sa.id = p.sale_id
         WHERE sa.shift_id = @ShiftId
           AND sa.status = 'COMPLETED'
           AND p.tender_type = @CashTenderType;
        """;

    private const string CashRefundsSql =
        """
        SELECT COALESCE(SUM(-p.amount), 0)
          FROM payment p
          JOIN sale_return sr ON sr.id = p.sale_return_id
         WHERE sr.shift_id = @ShiftId
           AND p.tender_type = @CashTenderType;
        """;

    private const string CashMovementTotalSql =
        """
        SELECT COALESCE(SUM(amount), 0)
          FROM cash_movement
         WHERE shift_id = @ShiftId
           AND direction = @Direction;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteCashMovementReader(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashMovementRecord>> ListForShiftAsync(
        long shiftId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                HistorySql, new { ShiftId = shiftId }, cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<CashMovementRecord> result = [.. rows.Select(ToRecord)];
            return result;
        }
    }

    /// <inheritdoc />
    public async Task<ShiftCashFigures> GetCashFiguresAsync(
        long shiftId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var openingFloatScaled = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition(OpeningFloatSql, new { ShiftId = shiftId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Shift {shiftId} does not exist."));

            var cashSalesScaled = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    CashSalesSql,
                    new { ShiftId = shiftId, CashTenderType },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var cashRefundsScaled = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    CashRefundsSql,
                    new { ShiftId = shiftId, CashTenderType },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var cashInScaled = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    CashMovementTotalSql,
                    new { ShiftId = shiftId, Direction = "IN" },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var cashOutScaled = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    CashMovementTotalSql,
                    new { ShiftId = shiftId, Direction = "OUT" },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return new ShiftCashFigures(
                Money.FromScaled(openingFloatScaled),
                Money.FromScaled(cashSalesScaled),
                Money.FromScaled(cashRefundsScaled),
                Money.FromScaled(cashInScaled),
                Money.FromScaled(cashOutScaled));
        }
    }

    private static CashMovementRecord ToRecord(Row row) => new(
        row.Id,
        row.ShiftId,
        row.Direction == "IN" ? CashMovementDirection.In : CashMovementDirection.Out,
        Money.FromScaled(row.AmountScaled),
        row.Reason,
        row.UserId,
        row.UserDisplayName,
        DateTimeOffset.ParseExact(
            row.OccurredAtText, Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture, DateTimeStyles.None));

    /// <summary>The flat shape Dapper maps a row of <see cref="HistorySql"/> onto.</summary>
    private sealed class Row
    {
        public long Id { get; set; }

        public long ShiftId { get; set; }

        public string Direction { get; set; } = string.Empty;

        public long AmountScaled { get; set; }

        public string Reason { get; set; } = string.Empty;

        public long UserId { get; set; }

        public string UserDisplayName { get; set; } = string.Empty;

        public string OccurredAtText { get; set; } = string.Empty;
    }
}
