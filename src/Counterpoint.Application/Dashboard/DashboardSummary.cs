using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Dashboard;

/// <summary>The compact home-screen dashboard (SRS FR-9.7).</summary>
/// <param name="TodaysSales">The sum of today's completed bills.</param>
/// <param name="BillCount">How many bills completed today.</param>
/// <param name="AverageBill"><see cref="TodaysSales"/> divided by <see cref="BillCount"/>, or zero with no bills yet.</param>
/// <param name="CashInDrawer">
/// The open shift's float plus its cash sales, or null when no shift is open. See
/// <see cref="Counterpoint.Application.Abstractions.Persistence.IDashboardReader.GetCashInDrawerAsync"/>
/// for exactly what this does and does not include in Phase 1.
/// </param>
/// <param name="LowStockCount">Active products at or below their configured reorder level.</param>
/// <param name="LastBackup">The most recent backup, or null when none has ever been taken.</param>
public sealed record DashboardSummary(
    Money TodaysSales,
    int BillCount,
    Money AverageBill,
    Money? CashInDrawer,
    int LowStockCount,
    LastBackupStatus? LastBackup);
