using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;
using VerifyXunit;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The fixed-layout bridge from a completed return to a byte stream (SRS FR-5, FR-7.1, task
/// P2-T02 step 4 and 5). This is the "prints a correct return receipt" half of AC-03 that
/// <c>CreateReturnTests</c> can only prove exists (a non-empty <c>print_job.payload</c>) - proving
/// what is actually on the paper is this renderer's own job, per CLAUDE.md's Devices testing
/// standard, and it must pass on Linux because it is bytes, not hardware.
/// </summary>
public sealed class EscPosReturnReceiptRendererTests
{
    private const long Pieces = 1;

    [Fact]
    public void AC_03_TheOriginalBillNumberAndThePriceOriginallyPaidBothReachThePaper()
    {
        var lines = Print(Receipt("CASH"));

        lines.Should().Contain(line => line.Contains("RTN-2026-000001", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("INV-2026-000001", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Galvanised bolt M8", StringComparison.Ordinal));

        // 2 pieces @ 12.50 refunded - never today's price, which the receipt never even sees
        // (SaleReturnReceiptLine carries no such field to print by mistake).
        lines.Should().Contain(line => line.Contains("12.50", StringComparison.Ordinal));
        lines.Should().Contain(line => line.TrimEnd().EndsWith("25.00", StringComparison.Ordinal));
    }

    [Fact]
    public void ARestockingFeeIsShownSeparatelyFromTheLineRefunds()
    {
        var withFee = Print(Receipt("CASH", restockingFee: Money.FromDecimal(2.50m)));

        withFee.Should().Contain(
            line => line.Contains("Restocking fee", StringComparison.Ordinal)
                && line.Contains("2.50", StringComparison.Ordinal),
            "task P2-T02 step 5: the fee is applied per policy and shown separately, not folded "
            + "silently into the subtotal or the total refund");

        var withoutFee = Print(Receipt("CASH", restockingFee: Money.Zero));

        withoutFee.Should().NotContain(
            line => line.Contains("Restocking fee", StringComparison.Ordinal),
            "no fee, no line about one - printing '0.00' would look like a policy that does not exist");
    }

    [Fact]
    public void ADamagedLineIsDistinguishedFromASellableOneOnTheReceipt()
    {
        var lines = Print(Receipt("CASH", disposition: "DAMAGED"));

        lines.Should().Contain(
            line => line.Contains("DAMAGED", StringComparison.Ordinal),
            "FR-5.8: the customer can see which of their returned items did not go back on the shelf");

        var sellableLines = Print(Receipt("CASH", disposition: "SELLABLE"));
        sellableLines.Should().NotContain(line => line.Contains("DAMAGED", StringComparison.Ordinal));
    }

    [Fact]
    public void ACashRefundOpensTheDrawerAndACardRefundDoesNot()
    {
        // ESC p 0 25 250 - the same drawer-kick sequence the sale receipt renderer emits.
        var kick = new byte[] { 0x1B, 0x70, 0x00, 25, 250 };

        FindSequence(Render(Receipt("CASH")), kick)
            .Should().BeGreaterThan(-1, "a cash refund pays out of the drawer (SRS FR-7.7)");

        FindSequence(Render(Receipt("CARD")), kick)
            .Should().Be(-1, "a card refund never touches the drawer");
    }

    [Fact]
    public void TheReturnNumberIsEncodedAsABarcodeSoARepeatVisitCanScanTheReturnSlip()
    {
        // GS k 73 (Code 128), then the length-prefixed data.
        var barcode = FindSequence(Render(Receipt("CASH")), [0x1D, 0x6B, 73]);

        barcode.Should().BeGreaterThan(-1, "the return number prints as a Code 128 symbol");
    }

    [Fact]
    public void TheReceiptEndsWithACut()
    {
        // GS V 1 - partial cut, PrinterCapabilities' default.
        FindSequence(Render(Receipt("CASH")), [0x1D, 0x56, 1]).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void PrintingThePolicyTextPutsTheSameWordsEnforcementUsedOnThePaper()
    {
        var lines = Print(Receipt("CASH", policyText: "Returns within 14 days with receipt only."));

        lines.Should().Contain(
            line => line.Contains("Returns within 14 days with receipt only.", StringComparison.Ordinal),
            "NFR-L3: the printed text and the enforced policy come from the same setting");
    }

    [Fact]
    public Task Srs_10_1_TheReturnReceiptRendersToTheCommittedByteStream()
    {
        var bytes = Render(Receipt("CASH"));

        return Verifier.Verify(EscPosDump.Describe(bytes)).UseDirectory("Snapshots");
    }

    private static byte[] Render(SaleReturnReceipt receipt) =>
        new EscPosReturnReceiptRenderer(new EscPosRenderer(), new HalfAwayFromZeroRounding(decimalPlaces: 2))
            .Render(receipt);

    private static System.Collections.Generic.List<string> Print(SaleReturnReceipt receipt) =>
        EscPosDump.PrintedLines(Render(receipt));

    private static SaleReturnReceipt Receipt(
        string refundMethod,
        Money? restockingFee = null,
        string disposition = "SELLABLE",
        string policyText = "") => new(
        "RTN-2026-000001",
        "INV-2026-000001",
        new DateTimeOffset(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5)),
        [
            new SaleReturnReceiptLine(
                "Galvanised bolt M8",
                Quantity.FromDecimal(2m, Pieces),
                "pc",
                Money.FromDecimal(12.50m),
                Money.FromDecimal(25.00m),
                disposition),
        ],
        Money.FromDecimal(25.00m),
        Money.Zero,
        restockingFee ?? Money.Zero,
        Money.FromDecimal(25.00m) - (restockingFee ?? Money.Zero),
        refundMethod,
        "Kamal",
        policyText);

    /// <summary>Index of the first occurrence of <paramref name="needle"/>, or -1.</summary>
    private static int FindSequence(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            if (Enumerable.Range(0, needle.Length).All(i => haystack[start + i] == needle[i]))
            {
                return start;
            }
        }

        return -1;
    }
}
