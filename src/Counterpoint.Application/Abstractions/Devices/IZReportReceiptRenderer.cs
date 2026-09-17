using Counterpoint.Application.Shifts;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a Z report into the byte stream a receipt printer eats (SRS FR-7.1, FR-8.4, task P3-T03
/// "Do this" #2).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="IXReportReceiptRenderer"/>: bytes in, bytes out, no I/O and no
/// device - safe to call from inside the close transaction, because rendering is not a printer
/// call (CLAUDE.md invariant 7). The rendered bytes are what <see cref="ICloseShift"/> hands
/// <see cref="IPrintJobOutbox.EnqueueAsync"/>; nothing here ever touches a printer.
/// </remarks>
public interface IZReportReceiptRenderer
{
    /// <summary>Renders one Z report.</summary>
    public byte[] Render(ZReportSummary report);
}
