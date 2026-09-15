using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Cash;

/// <summary>
/// The expected-drawer formula (SRS FR-8.1, FR-8.3, FR-8.4, task P3-T01 "Do this" #2):
/// <c>expected = opening_float + cash sales - cash refunds + cash in - cash out</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole of task P3-T01's own risk note.</b> "Two implementations of the
/// expected-cash formula, one in X and one in Z" is prevented by there being exactly one place the
/// arithmetic is written down - here - with exactly one test
/// (<c>ExpectedCashCalculatorTests.FR_8_1_ExpectedCashIsOpeningFloatPlusCashSalesLessRefundsPlusInLessOut</c>).
/// <see cref="Counterpoint.Application.Cash.IExpectedCashService"/> is the only caller today; the
/// X report (P3-T02) and the Z report (P3-T03) call that service rather than reimplementing this
/// method or reaching for the arithmetic themselves.
/// </para>
/// <para>
/// Pure evaluation only, the same shape as <c>Counterpoint.Domain.Pricing.DiscountCapPolicy</c>
/// and <c>Counterpoint.Domain.Returns.ReturnPolicy</c>: no I/O, no settings, no session - just the
/// arithmetic every caller must agree on.
/// </para>
/// </remarks>
public static class ExpectedCashCalculator
{
    /// <summary>
    /// What the drawer should hold right now, given the shift's own float and everything that has
    /// moved across it since.
    /// </summary>
    /// <param name="openingFloat"><c>shift.opening_float</c> - the cash counted in before trading started.</param>
    /// <param name="cashSales">The sum of cash tenders collected on completed sales this shift.</param>
    /// <param name="cashRefunds">The sum of cash paid out on returns this shift, as a positive magnitude.</param>
    /// <param name="cashIn">The sum of <see cref="CashMovementDirection.In"/> movements this shift.</param>
    /// <param name="cashOut">The sum of <see cref="CashMovementDirection.Out"/> movements this shift.</param>
    public static Money Calculate(
        Money openingFloat,
        Money cashSales,
        Money cashRefunds,
        Money cashIn,
        Money cashOut) =>
        openingFloat + cashSales - cashRefunds + cashIn - cashOut;
}
