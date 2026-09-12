namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Renders a goods receipt note as an A4 PDF document (SRS FR-4.7, FR-7.10: "the system must
/// print ... GRN").
/// </summary>
/// <remarks>
/// The same shape as <see cref="IPurchaseOrderDocumentRenderer"/>: pure, bytes out, no file, no
/// printer, no clock. A GRN is a back-office/supplier-facing document, never an 80 mm thermal
/// receipt (P2-T07's own "Do this" #4 points at the same A4/QuestPDF rendering capability P1-T11
/// and P2-T06 already built, not a new layout engine).
/// </remarks>
public interface IGoodsReceiptDocumentRenderer
{
    /// <summary>Renders one goods receipt as a PDF document.</summary>
    public byte[] RenderPdf(GoodsReceiptDocument document);
}
