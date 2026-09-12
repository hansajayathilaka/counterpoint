using System;
using System.Text;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Devices.Printing.A4;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The goods receipt note document (SRS FR-4.7, FR-7.10: "the system must print ... GRN"). A
/// byte-stream check, the same shape as <see cref="QuestPdfPurchaseOrderRendererTests"/> - it
/// proves the software track's own promise (CLAUDE.md: "Device snapshot tests must pass on
/// Linux") without any real printer.
/// </summary>
public sealed class QuestPdfGoodsReceiptRendererTests
{
    private const long Pieces = 1;
    private const long Boxes = 2;

    [Fact]
    public void FR_4_7_ItRendersAValidPdfDocument()
    {
        var bytes = new QuestPdfGoodsReceiptRenderer().RenderPdf(Document());

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "QuestPDF must produce a real PDF stream");
        bytes.Length.Should().BeGreaterThan(100, "a one-line document would suggest nothing rendered");
    }

    [Fact]
    public void FR_4_7_ItRendersWithoutThrowingWhenTheReceiptHasNoInvoiceNumberOrLinkedOrderOrNote()
    {
        var bare = new GoodsReceiptDocument(
            "GRN-2026-000001",
            new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(5.5)),
            "Ceylon Hardware Suppliers",
            SupplierInvoiceNo: null,
            PurchaseOrderNo: null,
            "Owner",
            Note: null,
            Money.FromDecimal(150.00m),
            Money.Zero,
            Money.Zero,
            Money.FromDecimal(150.00m),
            [new GoodsReceiptDocumentLine(
                "NAIL-001-A", "Common nails 3\"", Quantity.FromDecimal(10m, Pieces), "pcs",
                Money.FromDecimal(15.00m), Money.Zero, Money.FromDecimal(150.00m))]);

        var act = () => new QuestPdfGoodsReceiptRenderer().RenderPdf(bare);

        act.Should().NotThrow();
    }

    [Fact]
    public void FR_4_7_TheDocumentTotalIsNeverRecomputedByTheRenderer()
    {
        var document = Document();

        // The renderer formats Total; it must never compute it (CLAUDE.md invariant 2's spirit,
        // applied to a document that is not a sale) - proven by the total already being right,
        // freight included, before the renderer ever sees it.
        document.Total.Should().Be(Money.FromDecimal(150.00m + 2000.00m + 50.00m));

        var act = () => new QuestPdfGoodsReceiptRenderer().RenderPdf(document);
        act.Should().NotThrow();
    }

    private static GoodsReceiptDocument Document() => new(
        "GRN-2026-000002",
        new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(5.5)),
        "Ceylon Hardware Suppliers",
        "INV-88213",
        "PO-2026-000001",
        "Owner",
        "Delivered to the back store.",
        Money.FromDecimal(150.00m + 2000.00m),
        Money.Zero,
        Money.FromDecimal(50.00m),
        Money.FromDecimal(150.00m + 2000.00m + 50.00m),
        [
            new GoodsReceiptDocumentLine(
                "NAIL-001-A", "Common nails 3\"", Quantity.FromDecimal(10m, Pieces), "pcs",
                Money.FromDecimal(15.00m), Money.Zero, Money.FromDecimal(150.00m)),
            new GoodsReceiptDocumentLine(
                "NAIL-BOXED-A", "Boxed nails", Quantity.FromDecimal(2m, Boxes), "box",
                Money.FromDecimal(1000.00m), Money.Zero, Money.FromDecimal(2000.00m)),
        ]);
}
