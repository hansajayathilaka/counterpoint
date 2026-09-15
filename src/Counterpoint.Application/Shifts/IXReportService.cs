using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// The X report (task P3-T02, SRS FR-8.3, RPT-04): a mid-shift snapshot that changes nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only, all the way down.</b> <see cref="GenerateAsync"/> composes four existing reads -
/// <c>IXReportFiguresReader</c>, <c>ICashMovementReader</c>, <c>IShiftLookup</c> and
/// <c>IExpectedCashService</c> (the last one is P3-T01's single expected-cash formula, called
/// rather than reimplemented, per that task's own risk note) - and writes nothing: no
/// <c>audit_log</c> row, no <c>print_job</c> row, no touch of <c>shift</c> or any other table.
/// Task P3-T02's own risk note is explicit: "the important property is that it is non-clearing -
/// taking one must be free of side effects", proved by
/// <c>XReportServiceTests.P3_T02_TakingTenXReportsChangesNoDataWhatsoever</c>.
/// </para>
/// <para>
/// <b>Own shift only, for a cashier; any shift, for an owner.</b> The same rule
/// <see cref="Counterpoint.Application.Cash.ICashMovementService.GetHistoryAsync"/> already
/// enforces for a shift's cash-movement history (task P3-T01 "Do this" #4) - task P3-T02's own
/// "Done when": "a cashier can take one for their own shift but not for another user's".
/// </para>
/// </remarks>
public interface IXReportService
{
    /// <summary>Builds the X report for one shift, recomputed from the database on every call.</summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// Nobody is signed in, or a cashier asked for a shift other than the one they are currently
    /// trading in. An owner may ask for any shift.
    /// </exception>
    public Task<XReportSummary> GenerateAsync(long shiftId, CancellationToken cancellationToken = default);
}
