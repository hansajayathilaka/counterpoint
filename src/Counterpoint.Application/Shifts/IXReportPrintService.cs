using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// Prints an X report to the thermal printer (task P3-T02 "Do this" #2: "rendered to the thermal
/// printer and to screen").
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a <c>print_job</c> row.</b> Every other document in this codebase is enqueued to the
/// <c>print_job</c> outbox inside its own business transaction and drained later by
/// <c>PrintWorker</c> (CLAUDE.md invariant 7). An X report is not a document belonging to any
/// transaction - there is nothing to enqueue against, and writing a row purely to describe a
/// report that itself changes nothing would itself be the side effect task P3-T02's own risk note
/// warns against ("the important property is that it is non-clearing"). This calls
/// <see cref="IReceiptPrinter.PrintAsync"/> directly and synchronously instead, exactly as
/// <c>PrintWorker</c> does when its own turn comes, but with no outbox row before or after it.
/// </para>
/// <para>
/// <see cref="IXReportService.GenerateAsync"/> does the one piece of authorisation work (own
/// shift only, for a cashier) before either byte is rendered, so printing carries the identical
/// rule the on-screen report does.
/// </para>
/// </remarks>
public interface IXReportPrintService
{
    /// <summary>Builds, renders and prints the X report for one shift.</summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// Nobody is signed in, or a cashier asked for a shift other than the one they are currently
    /// trading in. An owner may ask for any shift.
    /// </exception>
    public Task<PrintOutcome> PrintAsync(long shiftId, CancellationToken cancellationToken = default);
}
