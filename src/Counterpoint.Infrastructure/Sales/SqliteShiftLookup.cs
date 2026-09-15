using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Answers <see cref="IShiftLookup"/> off a read connection - the same split
/// <see cref="Counterpoint.Infrastructure.Dashboard.SqliteDashboardReader"/> draws (task P3-T01).
/// </summary>
internal sealed class SqliteShiftLookup : IShiftLookup
{
    private const string Sql =
        """
        SELECT id AS ShiftId,
               shift_no AS ShiftNo,
               user_id AS UserId,
               opened_at AS OpenedAtText,
               business_date AS BusinessDateText,
               opening_float AS OpeningFloatScaled,
               status AS Status
          FROM shift
         WHERE id = @ShiftId;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteShiftLookup(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<ShiftSummary?> FindAsync(long shiftId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(Sql, new { ShiftId = shiftId }, cancellationToken: cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<Row>(command).ConfigureAwait(false);

            return row is null
                ? null
                : new ShiftSummary(
                    row.ShiftId,
                    row.ShiftNo,
                    row.UserId,
                    DateTimeOffset.ParseExact(
                        row.OpenedAtText,
                        Iso8601TimestampConverter.Format,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None),
                    DateOnly.ParseExact(row.BusinessDateText, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Money.FromScaled(row.OpeningFloatScaled),
                    row.Status);
        }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    private sealed class Row
    {
        public long ShiftId { get; set; }

        public string ShiftNo { get; set; } = string.Empty;

        public long UserId { get; set; }

        public string OpenedAtText { get; set; } = string.Empty;

        public string BusinessDateText { get; set; } = string.Empty;

        public long OpeningFloatScaled { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
