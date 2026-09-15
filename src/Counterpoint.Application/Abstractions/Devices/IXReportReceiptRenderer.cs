using Counterpoint.Application.Shifts;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns an X report snapshot into the byte stream a receipt printer eats (SRS FR-7.1, FR-8.3,
/// task P3-T02 "Do this" #2).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="ICashSlipRenderer"/>: bytes in, bytes out, no I/O and no device.
/// Rendering <see cref="XReportSummary"/> directly rather than a bespoke slip DTO is deliberate
/// here - unlike a cash-movement slip, which combines fields no single existing type already
/// carried, <see cref="XReportSummary"/> already <em>is</em> the read model a screen would bind
/// to, so there is nothing left to add for the printed copy and nothing that can drift between
/// the two views of the same non-clearing snapshot.
/// </remarks>
public interface IXReportReceiptRenderer
{
    /// <summary>Renders one X report.</summary>
    public byte[] Render(XReportSummary report);
}
