using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads the sales, returns, tax and tender figures an X report needs for one shift (task P3-T02
/// "Do this" #1, SRS FR-8.3, RPT-04).
/// </summary>
/// <remarks>
/// Hand-written SQL over a report read connection - the same split <see cref="ICashMovementReader"/>
/// draws for cash, except this one is answered from <c>Counterpoint.Reporting</c> through
/// <see cref="IReportConnectionFactory"/> rather than from <c>Counterpoint.Infrastructure</c>
/// through <c>IPosConnectionFactory</c>, because it is squarely a report query (CLAUDE.md "Project
/// boundaries"; the same seam <c>IStockValuationQuery</c> and <c>IReorderListQuery</c> already use,
/// first populated in task P2-T11). Every figure it returns is scoped to the one <c>shift_id</c>
/// asked for - never a whole-table scan of <c>sale</c>, <c>sale_return</c> or <c>payment</c>.
/// </remarks>
public interface IXReportFiguresReader
{
    /// <summary>The raw sales, returns, tax and tender figures for one shift.</summary>
    public Task<XReportFigures> GetAsync(long shiftId, CancellationToken cancellationToken = default);
}
