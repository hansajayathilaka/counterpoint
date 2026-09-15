using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The raw figures <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/> is
/// evaluated against for one shift (task P3-T01 "Do this" #2).
/// </summary>
/// <param name="OpeningFloat"><c>shift.opening_float</c>.</param>
/// <param name="CashSales">
/// The sum of cash tenders on this shift's own completed sales (<c>payment.tender_type = 'CASH'</c>,
/// <c>payment.sale_id IS NOT NULL</c>, joined through <c>sale.status = 'COMPLETED'</c>).
/// </param>
/// <param name="CashRefunds">
/// The sum of cash paid out on this shift's own returns, as a positive magnitude
/// (<c>payment.tender_type = 'CASH'</c>, <c>payment.sale_return_id IS NOT NULL</c>, joined through
/// <c>sale_return.shift_id</c> - a refund's own payment row carries a negative amount, negated here
/// so the figure reads as a magnitude the way <c>cash_movement.amount</c> already does).
/// </param>
/// <param name="CashIn">The sum of this shift's own <c>cash_movement</c> rows with <c>direction = 'IN'</c>.</param>
/// <param name="CashOut">The sum of this shift's own <c>cash_movement</c> rows with <c>direction = 'OUT'</c>.</param>
public sealed record ShiftCashFigures(
    Money OpeningFloat,
    Money CashSales,
    Money CashRefunds,
    Money CashIn,
    Money CashOut);
