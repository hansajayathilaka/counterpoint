using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Counterpoint.Devices.Printing.A4;

/// <summary>
/// The A4/A5 invoice, over the same <see cref="SaleReceipt"/> data the thermal receipt renders
/// (SRS FR-7.2, FR-7.9, PRT-09).
/// </summary>
/// <remarks>
/// <para>
/// A QuestPDF document, and nothing shared with <see cref="EscPosSaleReceiptRenderer"/> beyond
/// the data model - the task's own "Risks" note forbids trying to unify an 80 mm thermal layout
/// engine with an A4 one. Every amount printed here is a field already on <see cref="SaleReceipt"/>,
/// already rounded (CLAUDE.md invariant 2): this renderer formats, it never computes a total.
/// </para>
/// <para>
/// The signature area (PRT-09) prints only for a trade customer
/// (<see cref="SaleReceipt.IsTradeCustomer"/>) - a walk-in receipt has no account to sign for.
/// Full legal-field review, unified styling across every document type, and the share/export
/// action are P5-T04's ("A4 invoice and document polish"); this is the rendering capability
/// P5-T04's own context note says Phase 1 builds.
/// </para>
/// </remarks>
public sealed class QuestPdfSaleInvoiceRenderer : ISaleInvoiceRenderer
{
    static QuestPdfSaleInvoiceRenderer()
    {
        // QuestPDF requires exactly one license selection per process. Community is free for a
        // small business's own use, which is this product's whole audience (CLAUDE.md: a
        // single-cashier hardware shop). Set once, here, rather than in the composition root, so
        // every caller of this renderer - including a test that constructs it directly - gets a
        // working document without also having to know about QuestPDF's licensing API.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public byte[] RenderPdf(SaleReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text("TAX INVOICE").FontSize(18).Bold();
                    column.Item().Text("Bill No: " + receipt.BillNo);
                    column.Item().Text("Date: " + receipt.SoldAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    column.Item().Text("Customer: " + receipt.CustomerName);
                    column.Item().Text("Cashier: " + receipt.CashierName);
                });

                page.Content().PaddingVertical(1, Unit.Centimetre).Column(column =>
                {
                    column.Spacing(4);

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Item").Bold();
                            header.Cell().Text("Qty").Bold();
                            header.Cell().Text("Rate").Bold();
                            header.Cell().Text("Amount").Bold();
                        });

                        foreach (var line in receipt.Lines)
                        {
                            table.Cell().Text(line.Description);
                            table.Cell().Text(line.Quantity.Value.ToString("0.####", CultureInfo.InvariantCulture)
                                + " " + line.UomSymbol);
                            table.Cell().Text(line.UnitPrice.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                            table.Cell().Text(line.LineTotal.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                        }
                    });

                    column.Item().PaddingTop(10).AlignRight().Column(totals =>
                    {
                        totals.Item().Text("Sub total: " + receipt.Subtotal.Amount.ToString("0.00", CultureInfo.InvariantCulture));

                        if (!receipt.Discount.IsZero)
                        {
                            totals.Item().Text("Discount: -" + receipt.Discount.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                        }

                        totals.Item().Text("Taxable value: " + receipt.TaxableValue.Amount.ToString("0.00", CultureInfo.InvariantCulture));

                        foreach (var tax in receipt.TaxBreakdown)
                        {
                            totals.Item().Text(tax.Label + ": " + tax.TaxAmount.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                        }

                        totals.Item().PaddingTop(4).Text("TOTAL: " + receipt.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture))
                            .FontSize(14).Bold();
                    });

                    if (receipt.IsTradeCustomer)
                    {
                        // PRT-09 - a signature area for a trade customer's formal invoice.
                        column.Item().PaddingTop(40).Row(row =>
                        {
                            row.RelativeItem().Column(box =>
                            {
                                box.Item().LineHorizontal(0.5f);
                                box.Item().Text("Received by");
                            });

                            row.ConstantItem(40);

                            row.RelativeItem().Column(box =>
                            {
                                box.Item().LineHorizontal(0.5f);
                                box.Item().Text("Authorised signature");
                            });
                        });
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

        return document.GeneratePdf();
    }
}
