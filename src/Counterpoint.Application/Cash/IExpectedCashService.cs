using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Cash;

/// <summary>
/// Computes a shift's expected drawer total, the one door both the X report (P3-T02) and the Z
/// report (P3-T03) walk through (task P3-T01 "Do this" #2 and its own "Risks": "two implementations
/// of the expected-cash formula, one in X and one in Z" - there must be exactly one).
/// </summary>
/// <remarks>
/// The arithmetic itself is <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/>,
/// a pure function with its own single test. This service is the thin, injectable seam that reads
/// the figures that arithmetic needs off the database - the shape a future report task depends on,
/// rather than the raw <c>ICashMovementReader</c> and the domain calculator separately.
/// </remarks>
public interface IExpectedCashService
{
    /// <summary>The expected-drawer figures for one shift, recomputed from the database on every call.</summary>
    public Task<ExpectedCashSummary> CalculateAsync(long shiftId, CancellationToken cancellationToken = default);
}
