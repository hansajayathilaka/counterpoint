using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Dashboard;

/// <summary>
/// Composes <see cref="IDashboardReader"/> and <see cref="ILastBackupStatusReader"/> into the
/// six figures of the home-screen dashboard (SRS FR-9.7).
/// </summary>
/// <remarks>
/// <see cref="TimeProvider"/> decides "today" the same way <c>AuthenticationService</c> decides
/// "now" - a clock the test suite can hold fixed, not <c>DateTimeOffset.Now</c> read inline.
/// </remarks>
public sealed class DashboardService : IDashboardQueries
{
    private readonly IDashboardReader _reader;
    private readonly ILastBackupStatusReader _backup;
    private readonly TimeProvider _timeProvider;

    public DashboardService(IDashboardReader reader, ILastBackupStatusReader backup, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _reader = reader;
        _backup = backup;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var businessDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);

        var sales = await _reader.GetTodaysSalesAsync(businessDate, cancellationToken).ConfigureAwait(false);
        var cashInDrawer = await _reader.GetCashInDrawerAsync(cancellationToken).ConfigureAwait(false);
        var lowStockCount = await _reader.GetLowStockCountAsync(cancellationToken).ConfigureAwait(false);
        var lastBackup = await _backup.GetLastAsync(cancellationToken).ConfigureAwait(false);

        // Decimal division, never double or float (CLAUDE.md invariant 1) - Money.Divide wraps
        // plain decimal arithmetic. This is a dashboard figure, not a line or bill total, so it
        // is not one of the two rounding points IRoundingPolicy owns (CLAUDE.md invariant 2); the
        // screen formats it to the currency's display places the same way it formats every other
        // amount, without persisting this division anywhere.
        var averageBill = sales.BillCount > 0 ? sales.Total.Divide(sales.BillCount) : Money.Zero;

        return new DashboardSummary(sales.Total, sales.BillCount, averageBill, cashInDrawer, lowStockCount, lastBackup);
    }
}
