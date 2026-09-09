namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Renders a completed bill as an A4/A5 invoice PDF (SRS FR-7.2, FR-7.9) - for a trade customer
/// who needs a formal document, or any bill saved as PDF.
/// </summary>
/// <remarks>
/// <para>
/// A separate renderer from <see cref="ISaleReceiptRenderer"/> over the same
/// <see cref="SaleReceipt"/> data, deliberately (P1-T11's own "Risks" note): an 80 mm thermal
/// receipt and an A4 page do not share a layout engine, only a data model. Sharing totals with
/// the thermal receipt is by construction, not by re-derivation - both renderers read the same
/// already-rounded <see cref="SaleReceipt"/> fields and format them, neither recomputes one.
/// </para>
/// <para>
/// Pure: bytes in (nothing, actually - just the receipt), PDF bytes out. No file, no printer, no
/// clock. Safe to call inside a transaction, though nothing in Phase 1 does - the A4 path is
/// requested after a sale, from the print queue or a "save as PDF" action, never as part of
/// completing one.
/// </para>
/// </remarks>
public interface ISaleInvoiceRenderer
{
    /// <summary>Renders one bill as a PDF document.</summary>
    public byte[] RenderPdf(SaleReceipt receipt);
}
