using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads <c>cash_movement</c> and the figures the expected-cash calculation is built from
/// (task P3-T01 "Do this" #2 and #4).
/// </summary>
/// <remarks>
/// Hand-written SQL over a read connection, not EF - the same split <see cref="IDashboardReader"/>
/// and <c>IAdjustmentHistoryQuery</c> draw (CLAUDE.md "Stack"): neither method here may compete
/// with the single write connection a sale or a cash movement is using.
/// </remarks>
public interface ICashMovementReader
{
    /// <summary>Every cash movement recorded against one shift, newest first (task P3-T01 "Do this" #4).</summary>
    public Task<IReadOnlyList<CashMovementRecord>> ListForShiftAsync(
        long shiftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The raw figures <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/>
    /// needs for one shift.
    /// </summary>
    public Task<ShiftCashFigures> GetCashFiguresAsync(
        long shiftId, CancellationToken cancellationToken = default);
}
