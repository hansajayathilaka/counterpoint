using System;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Sends an already-rendered document to a shelf-label printer (SRS FR-2.10, FR-2.12).
///
/// A separate port from <see cref="IReceiptPrinter"/>, deliberately: most shelf-label printers
/// speak TSPL/ZPL/EPL rather than ESC/POS, and even where the byte protocol coincided the two
/// devices are different physical printers on the counter, named by their own setting
/// (<c>PeripheralSettings.LabelPrinterName</c>). Counterpoint.Devices supplies the
/// implementations - a file writer for Linux development and CI, the Windows raw spooler on the
/// shop terminal (<c>HW-T03</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This never throws for a printer problem</b>, the same rule <see cref="IReceiptPrinter"/>
/// follows (CLAUDE.md invariant 7). Out of labels, unplugged, no permission on the spool folder:
/// all of it comes back as a failed <see cref="PrintOutcome"/> that the caller shows as a
/// warning. An exception from an implementation is a bug in that implementation.
/// </para>
/// <para>
/// <b>Unlike a receipt, printing a label is never inside the sale transaction</b>, so there is no
/// <c>print_job</c> outbox row behind this call. Label printing is an owner-initiated,
/// stand-alone action - print this product list, this search result, this GRN batch - with no
/// business transaction it needs to survive a printer fault: there is nothing to keep durable if
/// the printer is unavailable, only a batch to retry once it is back. The outbox exists to let a
/// sale complete without waiting on a printer (CLAUDE.md invariant 7); a label print has no sale
/// to complete, so <c>Counterpoint.Application.Labels.ILabelPrintService</c> calls this directly.
/// It still never runs inside a database transaction, for the same reason no printer call ever
/// does: a printer than can take seconds to answer must not hold the till's single writer open.
/// </para>
/// </remarks>
public interface ILabelPrinter
{
    /// <summary>
    /// Sends one batch of labels to the printer.
    /// </summary>
    /// <param name="document">The rendered byte stream, ready for the printer.</param>
    /// <param name="jobName">
    /// A short name for the job. Used for the spooler job title and for the development file
    /// name, so it should identify the batch to a human.
    /// </param>
    /// <param name="cancellationToken">Cancels the write. Cancellation is not a print failure.</param>
    /// <returns>Whether the document reached the printer, and where it went or why it did not.</returns>
    public Task<PrintOutcome> PrintAsync(
        ReadOnlyMemory<byte> document,
        string jobName,
        CancellationToken cancellationToken = default);
}
