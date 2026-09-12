namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Renders a purchase order as an A4 PDF document (SRS FR-4.5: "PO must be printable/exportable").
/// </summary>
/// <remarks>
/// The same shape as <see cref="ISaleInvoiceRenderer"/>: pure, bytes out, no file, no printer, no
/// clock. A purchase order is an internal/supplier-facing back-office document, never an 80 mm
/// thermal receipt, so there is no ESC/POS counterpart to keep in step with (P2-T06's own "Do
/// this" #4 points at the same A4/QuestPDF rendering capability P1-T11 already built for the
/// sale invoice, not a new layout engine).
/// </remarks>
public interface IPurchaseOrderDocumentRenderer
{
    /// <summary>Renders one purchase order as a PDF document.</summary>
    public byte[] RenderPdf(PurchaseOrderDocument document);
}
