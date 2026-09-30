using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="IShiftVarianceHistoryQuery"/>: the shift and cash-variance history (task P3-T06 "Do this" #4,
/// SRS RPT-21, FR-8.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c>. It reads the close fields of
/// <c>shift</c> exactly as they were stored at close - counted, expected, variance, note, closed_by - and
/// recomputes none of them (the expected cash is frozen at close and never re-derived). Everything derived
/// (over/short totals, cumulative variance, mean absolute variance, the trend) is computed here in C#.
/// </para>
/// <para>
/// The note threshold shown is <c>policy.shift_close_variance_note_threshold</c> as configured now - the same
/// setting <c>CloseShiftHandler</c> reads - so a shift is flagged when its absolute variance is above what the
/// close flow would demand a note for today; a threshold changed since is the owner's to bear in mind.
/// </para>
/// </remarks>
internal sealed class ShiftVarianceHistoryQuery : IShiftVarianceHistoryQuery
{
    /// <summary>Same fixed-width ISO-8601 shape the timestamp converter writes; see <c>SlowMovingStockQuery</c>.</summary>
    private const string Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private const string Sql =
        """
        SELECT sh.id AS ShiftId,
               sh.shift_no AS ShiftNo,
               sh.business_date AS BusinessDate,
               opener.display_name AS OpenedBy,
               COALESCE(closer.display_name, '') AS ClosedBy,
               sh.closed_at AS ClosedAtText,
               COALESCE(sh.counted_cash, 0) AS CountedCashScaled,
               COALESCE(sh.expected_cash, 0) AS ExpectedCashScaled,
               COALESCE(sh.variance, 0) AS VarianceScaled,
               sh.note AS Note
          FROM shift sh
          JOIN app_user opener ON opener.id = sh.user_id
          LEFT JOIN app_user closer ON closer.id = sh.closed_by
         WHERE sh.status = 'CLOSED'
           AND sh.business_date >= @From AND sh.business_date <= @To
         ORDER BY sh.business_date, sh.closed_at, sh.id;
        """;

    private readonly IReportConnectionFactory _connectionFactory;
    private readonly ISettings _settings;

    public ShiftVarianceHistoryQuery(IReportConnectionFactory connectionFactory, ISettings settings)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionFactory = connectionFactory;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<ShiftVarianceHistory> GetHistoryAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new
        {
            From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        List<Row> source;
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            source = (await connection.QueryAsync<Row>(
                new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
        }

        var threshold = _settings.Policy.ShiftCloseVarianceNoteThreshold;
        var rows = new List<ShiftVarianceRow>(source.Count);
        var cumulative = Money.Zero;

        foreach (var row in source)
        {
            var variance = Money.FromScaled(row.VarianceScaled);
            cumulative += variance;

            rows.Add(new ShiftVarianceRow(
                row.ShiftId,
                row.ShiftNo,
                DateOnly.ParseExact(row.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.OpenedBy,
                row.ClosedBy,
                DateTimeOffset.ParseExact(row.ClosedAtText, Iso8601Format, CultureInfo.InvariantCulture, DateTimeStyles.None),
                Money.FromScaled(row.CountedCashScaled),
                Money.FromScaled(row.ExpectedCashScaled),
                variance,
                string.IsNullOrWhiteSpace(row.Note) ? null : row.Note,
                variance.Abs() > threshold,
                cumulative));
        }

        var absolute = rows.Select(row => row.Variance.Abs()).ToList();
        var (trend, earlier, later) = Trend(absolute);

        return new ShiftVarianceHistory(
            range,
            threshold,
            rows,
            CanonicalFigures.Sum(rows.Select(row => row.Variance)),
            CanonicalFigures.Sum(rows.Where(row => row.Variance > Money.Zero).Select(row => row.Variance)),
            CanonicalFigures.Sum(rows.Where(row => row.Variance < Money.Zero).Select(row => row.Variance)),
            rows.Count == 0 ? Money.Zero : CanonicalFigures.Sum(absolute) / rows.Count,
            rows.Count(row => row.ExceedsNoteThreshold),
            trend,
            earlier,
            later);
    }

    /// <summary>Earlier-half against later-half mean absolute variance; needs at least four shifts.</summary>
    private static (VarianceTrend Trend, Money Earlier, Money Later) Trend(List<Money> absolute)
    {
        if (absolute.Count < 4)
        {
            return (VarianceTrend.NotEnoughData, Money.Zero, Money.Zero);
        }

        var half = absolute.Count / 2;
        var earlier = CanonicalFigures.Sum(absolute.Take(half)) / half;
        var later = CanonicalFigures.Sum(absolute.Skip(absolute.Count - half)) / half;

        var trend = later < earlier ? VarianceTrend.Improving
            : later > earlier ? VarianceTrend.Worsening
            : VarianceTrend.Steady;

        return (trend, earlier, later);
    }

    private sealed class Row
    {
        public long ShiftId { get; set; }

        public string ShiftNo { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public string OpenedBy { get; set; } = string.Empty;

        public string ClosedBy { get; set; } = string.Empty;

        public string ClosedAtText { get; set; } = string.Empty;

        public long CountedCashScaled { get; set; }

        public long ExpectedCashScaled { get; set; }

        public long VarianceScaled { get; set; }

        public string? Note { get; set; }
    }
}
