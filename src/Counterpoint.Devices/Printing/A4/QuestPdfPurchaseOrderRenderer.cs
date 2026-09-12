using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Counterpoint.Devices.Printing.A4;

/// <summary>
/// The purchase order document (SRS FR-4.5: "PO must be printable/exportable").
/// </summary>
/// <remarks>
/// The same QuestPDF approach as <see cref="QuestPdfSaleInvoiceRenderer"/>, over a different data
/// model - a purchase order is a back-office/supplier-facing document, never an 80 mm thermal
/// receipt (P2-T06's own "Do this" #4 points at the A4 rendering capability P1-T11 already built,
/// not a new layout engine). Every amount printed here is a field already on
/// <see cref="PurchaseOrderDocument"/>: this renderer formats, it never computes a total.
/// </remarks>
public sealed class QuestPdfPurchaseOrderRenderer : IPurchaseOrderDocumentRenderer
{
    static QuestPdfPurchaseOrderRenderer()
    {
        // QuestPDF requires exactly one license selection per process; setting it again here,
        // exactly as QuestPdfSaleInvoiceRenderer's own static constructor does, is idempotent and
        // means this renderer produces a working document even when constructed on its own (a
        // test, for instance) rather than always alongside the sale invoice renderer.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public byte[] RenderPdf(PurchaseOrderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text("PURCHASE ORDER").FontSize(18).Bold();
                    column.Item().Text("PO No: " + document.PoNo);
                    column.Item().Text("Date: " + document.OrderedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                    if (document.ExpectedAt is { } expected)
                    {
                        column.Item().Text("Expected: " + expected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }

                    column.Item().Text("Status: " + document.Status);
                    column.Item().Text("Supplier: " + document.SupplierName);

                    if (!string.IsNullOrWhiteSpace(document.SupplierAddress))
                    {
                        column.Item().Text(document.SupplierAddress!);
                    }

                    if (!string.IsNullOrWhiteSpace(document.SupplierPhone))
                    {
                        column.Item().Text("Tel: " + document.SupplierPhone);
                    }

                    column.Item().Text("Raised by: " + document.RaisedByName);
                });

                page.Content().PaddingVertical(1, Unit.Centimetre).Column(column =>
                {
                    column.Spacing(4);

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("SKU").Bold();
                            header.Cell().Text("Item").Bold();
                            header.Cell().Text("Qty").Bold();
                            header.Cell().Text("Cost").Bold();
                            header.Cell().Text("Amount").Bold();
                        });

                        foreach (var line in document.Lines)
                        {
                            table.Cell().Text(line.Sku);
                            table.Cell().Text(line.Description);
                            table.Cell().Text(line.Qty.Value.ToString("0.####", CultureInfo.InvariantCulture)
                                + " " + line.UomSymbol);
                            table.Cell().Text(line.UnitCost.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                            table.Cell().Text(line.LineTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                        }
                    });

                    column.Item().PaddingTop(10).AlignRight().Text(
                        "TOTAL: " + document.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture))
                        .FontSize(14).Bold();

                    if (!string.IsNullOrWhiteSpace(document.Note))
                    {
                        column.Item().PaddingTop(10).Text("Note: " + document.Note);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

        return pdf.GeneratePdf();
    }
}
