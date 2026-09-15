using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Cash;

namespace Counterpoint.Application.Cash;

/// <summary>
/// <see cref="IExpectedCashService"/>: reads <see cref="ShiftCashFigures"/> for one shift and
/// applies <see cref="ExpectedCashCalculator.Calculate"/> - nothing else (task P3-T01 "Do this" #2).
/// </summary>
public sealed class ExpectedCashService : IExpectedCashService
{
    private readonly ICashMovementReader _reader;

    public ExpectedCashService(ICashMovementReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <inheritdoc />
    public async Task<ExpectedCashSummary> CalculateAsync(
        long shiftId, CancellationToken cancellationToken = default)
    {
        var figures = await _reader.GetCashFiguresAsync(shiftId, cancellationToken).ConfigureAwait(false);

        var expected = ExpectedCashCalculator.Calculate(
            figures.OpeningFloat, figures.CashSales, figures.CashRefunds, figures.CashIn, figures.CashOut);

        return new ExpectedCashSummary(
            shiftId,
            figures.OpeningFloat,
            figures.CashSales,
            figures.CashRefunds,
            figures.CashIn,
            figures.CashOut,
            expected);
    }
}
