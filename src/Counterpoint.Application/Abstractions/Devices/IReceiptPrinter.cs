using System;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Sends an already-rendered document to a receipt printer (SRS FR-7.1).
///
/// The port takes bytes, not a receipt: rendering is the device layer's business and the
/// ESC/POS command set must not leak into the application. Counterpoint.Devices supplies the
/// implementations - a file writer for Linux development and CI, the Windows raw spooler on
/// the shop terminal (HW-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>This never throws for a printer problem</b> (SRS FR-7.8, AC-16, CLAUDE.md invariant 7).
/// Out of paper, unplugged, offline, no permission on the spool folder: all of it comes back
/// as a failed <see cref="PrintOutcome"/> that the caller logs and shows as a warning. The
/// sale is already committed by the time anything gets here, and nothing about printing may
/// undo it. An exception from an implementation is a bug in that implementation.
/// </para>
/// <para>
/// It is equally never called inside a database transaction. The print job is a row in the
/// <c>print_job</c> outbox written inside the sale transaction; a background worker picks it
/// up afterwards and calls this.
/// </para>
/// </remarks>
public interface IReceiptPrinter
{
    /// <summary>
    /// Sends one document to the printer.
    /// </summary>
    /// <param name="document">The rendered byte stream, ready for the printer.</param>
    /// <param name="jobName">
    /// A short name for the job - typically the bill number. Used for the spooler job title
    /// and for the development file name, so it should identify the document to a human.
    /// </param>
    /// <param name="cancellationToken">Cancels the write. Cancellation is not a print failure.</param>
    /// <returns>Whether the document reached the printer, and where it went or why it did not.</returns>
    public Task<PrintOutcome> PrintAsync(
        ReadOnlyMemory<byte> document,
        string jobName,
        CancellationToken cancellationToken = default);
}
