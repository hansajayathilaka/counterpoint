using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// <see cref="IXReportService"/>: composes four existing reads into one non-clearing snapshot
/// (task P3-T02 "Do this" #1).
/// </summary>
public sealed class XReportService : IXReportService
{
    private readonly IXReportFiguresReader _figures;
    private readonly ICashMovementReader _cashMovements;
    private readonly IShiftLookup _shifts;
    private readonly IExpectedCashService _expectedCash;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public XReportService(
        IXReportFiguresReader figures,
        ICashMovementReader cashMovements,
        IShiftLookup shifts,
        IExpectedCashService expectedCash,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(cashMovements);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(expectedCash);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _figures = figures;
        _cashMovements = cashMovements;
        _shifts = shifts;
        _expectedCash = expectedCash;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<XReportSummary> GenerateAsync(long shiftId, CancellationToken cancellationToken = default)
    {
        RequireCallerMayViewShift(shiftId);

        var shift = await _shifts.FindAsync(shiftId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Shift {shiftId} does not exist."));

        var figures = await _figures.GetAsync(shiftId, cancellationToken).ConfigureAwait(false);
        var cashMovements = await _cashMovements.ListForShiftAsync(shiftId, cancellationToken).ConfigureAwait(false);
        var expectedCash = await _expectedCash.CalculateAsync(shiftId, cancellationToken).ConfigureAwait(false);

        var generatedAt = _timeProvider.GetLocalNow();

        return new XReportSummary(
            shiftId,
            shift.ShiftNo,
            shift.UserId,
            shift.CashierDisplayName,
            shift.OpenedAt,
            generatedAt,
            generatedAt - shift.OpenedAt,
            shift.OpeningFloat,
            figures.SalesCount,
            figures.SalesValue,
            figures.ReturnsCount,
            figures.ReturnsValue,
            figures.DiscountTotal,
            figures.SalesTaxTotal,
            figures.ReturnsTaxTotal,
            figures.TaxBreakdown,
            figures.Tenders,
            cashMovements,
            expectedCash);
    }

    /// <summary>
    /// Task P3-T02 "Done when": "a cashier can take one for their own shift but not for another
    /// user's" - the same rule <see cref="ICashMovementService.GetHistoryAsync"/> already enforces
    /// (task P3-T01 "Do this" #4), reused rather than reinvented.
    /// </summary>
    private void RequireCallerMayViewShift(long shiftId)
    {
        var caller = _session.CurrentUser ?? throw new NotAuthorisedException(
            "Nobody is signed in. Sign in before taking an X report.");

        if (caller.Role != Role.Owner && shiftId != _session.ShiftId)
        {
            throw new NotAuthorisedException(
                "A cashier may only take an X report for the shift they are currently trading in.");
        }
    }
}
