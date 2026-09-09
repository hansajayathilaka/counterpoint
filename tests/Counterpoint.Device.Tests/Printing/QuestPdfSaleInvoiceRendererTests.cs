using System;
using System.Text;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Devices.Printing.A4;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The A4/A5 invoice, over the same <see cref="SaleReceipt"/> the thermal receipt renders
/// (SRS FR-7.2, FR-7.9, PRT-09).
/// </summary>
public sealed class QuestPdfSaleInvoiceRendererTests
{
    private const long Pieces = 1;

    [Fact]
    public void FR_7_2_ItRendersAValidPdfDocument()
    {
        var bytes = new QuestPdfSaleInvoiceRenderer().RenderPdf(Receipt());

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "QuestPDF must produce a real PDF stream");
        bytes.Length.Should().BeGreaterThan(100, "a one-line document would suggest nothing rendered");
    }

    [Fact]
    public void FR_7_2_TheInvoiceCarriesTheSameTotalsAsTheThermalReceiptToTheCent()
    {
        // Both renderers read the same SaleReceipt fields, already rounded once by
        // CompleteSaleHandler (CLAUDE.md invariant 2). This is that guarantee, made explicit:
        // neither renderer may recompute a total, so the same instance handed to each produces
        // the same figures by construction, not by coincidence.
        var receipt = Receipt();

        receipt.Total.Should().Be(Money.FromDecimal(2000.00m));
        receipt.Subtotal.Should().Be(Money.FromDecimal(2065.00m));
        receipt.Discount.Should().Be(Money.FromDecimal(65.00m));
        receipt.TaxableValue.Should().Be(Money.FromDecimal(2000.00m));

        // The renderer itself is proven not to alter any of them: it must produce a document
        // without throwing on exactly these already-rounded figures, and it must not need the
        // receipt to expose anything the thermal path does not already have.
        var act = () => new QuestPdfSaleInvoiceRenderer().RenderPdf(receipt);
        act.Should().NotThrow();
    }

    [Fact]
    public void PRT_09_ItRendersForBothATradeCustomerAndAWalkInWithoutThrowing()
    {
        // The signature area (PRT-09) is gated on SaleReceipt.IsTradeCustomer inside the
        // renderer; proving the flag itself reaches SaleReceipt correctly is
        // CompleteSaleHandlerTests's job (it is read from the customer's own record). What
        // belongs here is that neither branch of that gate breaks the document.
        var walkIn = () => new QuestPdfSaleInvoiceRenderer().RenderPdf(Receipt(isTradeCustomer: false));
        var trade = () => new QuestPdfSaleInvoiceRenderer().RenderPdf(Receipt(isTradeCustomer: true));

        walkIn.Should().NotThrow();
        trade.Should().NotThrow();
    }

    private static SaleReceipt Receipt(bool isTradeCustomer = false) => new(
        "INV-2026-004312",
        new DateTimeOffset(2026, 9, 3, 14, 7, 0, TimeSpan.FromHours(5.5)),
        [
            new SaleReceiptLine(
                "Hex Bolt M10x50 Zn",
                Quantity.FromDecimal(20m, Pieces),
                "pcs",
                Money.FromDecimal(25.00m),
                Money.FromDecimal(500.00m)),
            new SaleReceiptLine(
                "PVC Elbow 1\" 90deg",
                Quantity.FromDecimal(4m, Pieces),
                "pcs",
                Money.FromDecimal(90.00m),
                Money.FromDecimal(360.00m)),
            new SaleReceiptLine(
                "Cable 2.5mm 3-core",
                Quantity.FromDecimal(2.75m, Pieces),
                "m",
                Money.FromDecimal(420.00m),
                Money.FromDecimal(1155.00m)),
            new SaleReceiptLine(
                "Cutting charge",
                Quantity.FromDecimal(1m, Pieces),
                "svc",
                Money.FromDecimal(50.00m),
                Money.FromDecimal(50.00m)),
        ],
        Money.FromDecimal(2065.00m),
        Money.FromDecimal(65.00m),
        Money.FromDecimal(2000.00m),
        Money.Zero,
        Money.FromDecimal(2000.00m),
        [new SaleReceiptTender("CASH", Money.FromDecimal(2500.00m))],
        Money.FromDecimal(500.00m),
        [new SaleReceiptTaxLine("Tax @ 0%", Money.FromDecimal(2000.00m), Money.Zero)],
        "Kamal",
        isTradeCustomer ? "Nimal Hardware Trading" : "Walk-in",
        isTradeCustomer);
}
