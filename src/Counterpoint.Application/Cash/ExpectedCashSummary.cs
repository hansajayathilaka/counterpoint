using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Cash;

/// <summary>
/// A shift's expected-drawer figures, ready for the X and Z reports to display (task P3-T01
/// "Do this" #2, SRS FR-8.3, FR-8.4).
/// </summary>
/// <param name="ShiftId">The shift these figures were computed for.</param>
/// <param name="OpeningFloat"><c>shift.opening_float</c>.</param>
/// <param name="CashSales">The sum of cash tenders on this shift's own completed sales.</param>
/// <param name="CashRefunds">The sum of cash paid out on this shift's own returns, as a positive magnitude.</param>
/// <param name="CashIn">The sum of this shift's own cash-in movements.</param>
/// <param name="CashOut">The sum of this shift's own cash-out movements.</param>
/// <param name="ExpectedCash">
/// <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/> applied to the five
/// figures above - the one number an X report displays and a Z report reconciles a physical count
/// against.
/// </param>
public sealed record ExpectedCashSummary(
    long ShiftId,
    Money OpeningFloat,
    Money CashSales,
    Money CashRefunds,
    Money CashIn,
    Money CashOut,
    Money ExpectedCash);
