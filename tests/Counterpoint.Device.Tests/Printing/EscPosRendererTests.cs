using System;
using System.Linq;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using FluentAssertions;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The renderer's layout and command rules (SRS §10.2 PRT-01..PRT-05, FR-7.1, FR-7.7).
/// Every one of these runs on Linux against bytes; none of them needs a printer.
/// </summary>
public sealed class EscPosRendererTests
{
    private const int EightyMillimetreColumns = 48;

    [Fact]
    public void FR_7_1_EveryDocumentStartsByInitialisingThePrinter()
    {
        var bytes = Render(new ReceiptNode.TextLine("x"));

        bytes.Take(2).Should().Equal(
            new byte[] { EscPos.Esc, (byte)'@' },
            "a document must not inherit alignment or emphasis from the one before it");
    }

    [Fact]
    public void PRT_02_ALongDescriptionWrapsOntoAContinuationLine()
    {
        var bytes = Render(new ReceiptNode.TextLine(
            "Galvanised threaded rod M12 x 1000mm with two nuts and washers"));

        var lines = EscPosDump.PrintedLines(bytes);

        lines.Should().HaveCount(2);
        lines.Should().AllSatisfy(line => line.Length.Should().BeLessThanOrEqualTo(EightyMillimetreColumns));
        string.Join(' ', lines).Should().Be(
            "Galvanised threaded rod M12 x 1000mm with two nuts and washers",
            "wrapping must not lose or reorder a single word of the description");
    }

    [Fact]
    public void PRT_02_AWordLongerThanThePaperIsSplitRatherThanTruncated()
    {
        var partNumber = new string('A', 60);

        var lines = EscPosDump.PrintedLines(Render(new ReceiptNode.TextLine(partNumber)));

        string.Concat(lines).Should().Be(
            partNumber,
            "losing the tail of a part number is worse than an ugly line break");
        lines[0].Should().HaveLength(EightyMillimetreColumns);
    }

    [Fact]
    public void AmountsAreRightAlignedInAFixedMoneyColumn()
    {
        var lines = EscPosDump.PrintedLines(Render(
            new ReceiptNode.Columns("Sub total", "2065.00"),
            new ReceiptNode.Columns("Discount", "-65.00"),
            new ReceiptNode.Columns("TOTAL", "2000.00")));

        lines.Should().AllSatisfy(line => line.Length.Should().Be(EightyMillimetreColumns));
        lines[0].Should().EndWith("    2065.00");
        lines[1].Should().EndWith("     -65.00");
        lines[2].Should().EndWith("    2000.00");
        lines.Select(line => line.TrimEnd().Length).Distinct().Should().ContainSingle(
            "every amount ends in the same column, so the decimal points line up down the bill");
    }

    [Fact]
    public void AColumnDescriptionWrapsAndTheAmountLandsOnTheLastLine()
    {
        var lines = EscPosDump.PrintedLines(Render(new ReceiptNode.Columns(
            "Deposit refundable on return of the empty gas cylinder within 30 days",
            "1500.00")));

        lines.Should().HaveCountGreaterThan(1);
        lines[^1].Should().EndWith("1500.00");
        lines.SkipLast(1).Should().AllSatisfy(
            line => line.Should().NotContain("1500.00", "the amount prints once, on the last line"));
    }

    [Fact]
    public void AnAmountTooWideForThePaperIsTruncatedRatherThanWrapped()
    {
        var lines = EscPosDump.PrintedLines(Render(
            new ReceiptNode.Columns("Total", new string('9', 60))));

        lines.Should().ContainSingle("an amount is never re-flowed onto a second line");
        lines[0].Should().HaveLength(EightyMillimetreColumns);
    }

    [Fact]
    public void PRT_01_FiftyEightMillimetrePaperIsAConfigurationChange()
    {
        var bytes = new EscPosRenderer(PrinterCapabilities.FiftyEightMillimetre).Render(
            ReceiptDocument.Of(
                new ReceiptNode.Divider(),
                new ReceiptNode.Columns("Sub total", "2065.00")));

        var lines = EscPosDump.PrintedLines(bytes);

        lines[0].Should().HaveLength(32, "58 mm paper is 32 characters at font A");
        lines[1].Should().HaveLength(32).And.EndWith("   2065.00");
    }

    [Fact]
    public void PRT_03_TheTotalPrintsDoubleHeightAndDoubleWidth()
    {
        var bytes = Render(new ReceiptNode.Columns(
            "TOTAL",
            "2000.00",
            Bold: true,
            DoubleHeight: true,
            DoubleWidth: true));

        Contains(bytes, EscPos.Gs, (byte)'!', 0x11).Should().BeTrue("double width and double height");
        Contains(bytes, EscPos.Gs, (byte)'!', 0x00).Should().BeTrue("and back to normal afterwards");
        Contains(bytes, EscPos.Esc, (byte)'E', 1).Should().BeTrue("emphasised");

        EscPosDump.PrintedLines(bytes)[0].Should().HaveLength(
            EightyMillimetreColumns / 2,
            "double-width characters are twice as wide, so half as many fit");
    }

    [Fact]
    public void PRT_03_APrinterWithoutDoubleSizeStillPrintsTheTotalFullWidth()
    {
        var bytes = new EscPosRenderer(PrinterCapabilities.Default with { SupportsDoubleSize = false })
            .Render(ReceiptDocument.Of(new ReceiptNode.Columns(
                "TOTAL",
                "2000.00",
                DoubleHeight: true,
                DoubleWidth: true)));

        Contains(bytes, EscPos.Gs, (byte)'!').Should().BeFalse("the command is not sent at all");
        EscPosDump.PrintedLines(bytes)[0].Should().HaveLength(EightyMillimetreColumns);
    }

    [Fact]
    public void PRT_05_TheCutterFiresAfterFeedingThePaperClear()
    {
        var bytes = Render(new ReceiptNode.Cut());

        Contains(bytes, EscPos.Esc, (byte)'d', 4).Should().BeTrue("feed the print clear of the cutter");
        Contains(bytes, EscPos.Gs, (byte)'V', 1).Should().BeTrue("partial cut");
    }

    [Fact]
    public void PRT_05_APrinterWithNoCutterStillPrintsTheBill()
    {
        var capabilities = PrinterCapabilities.Default with { SupportsCut = false };

        var bytes = new EscPosRenderer(capabilities).Render(
            ReceiptDocument.Of(new ReceiptNode.TextLine("Thank you"), new ReceiptNode.Cut()));

        Contains(bytes, EscPos.Gs, (byte)'V').Should().BeFalse();
        EscPosDump.PrintedLines(bytes).Should().ContainSingle().Which.Should().Be("Thank you");
    }

    [Fact]
    public void PRT_05_TheCutCommandItselfIsACapability()
    {
        var capabilities = PrinterCapabilities.Default with { CutMode = CutMode.FeedAndPartial };

        var bytes = new EscPosRenderer(capabilities).Render(ReceiptDocument.Of(new ReceiptNode.Cut()));

        Contains(bytes, EscPos.Gs, (byte)'V', 66, 4).Should().BeTrue(
            "a printer that wants GS V 66 n is a settings change, not a renderer change");
        Contains(bytes, EscPos.Esc, (byte)'d', 4).Should().BeFalse("that command feeds by itself");
    }

    [Fact]
    public void FR_7_7_TheDrawerKickIsEscPZeroTwentyFiveTwoFifty()
    {
        var bytes = Render(new ReceiptNode.Kick());

        Contains(bytes, EscPos.Esc, (byte)'p', 0, 25, 250).Should().BeTrue();
    }

    [Fact]
    public void FR_7_7_APrinterWithNoDrawerPortSkipsTheKick()
    {
        var capabilities = PrinterCapabilities.Default with { SupportsDrawerKick = false };

        var bytes = new EscPosRenderer(capabilities).Render(ReceiptDocument.Of(new ReceiptNode.Kick()));

        bytes.Should().NotContain(
            (byte)'p',
            "the kick is skipped, and skipping it must not leave a stray byte in the stream");
    }

    [Fact]
    public void PRT_04_TheBillNumberPrintsAsANativeCode128Barcode()
    {
        var bytes = Render(new ReceiptNode.Barcode("INV-2026-004312"));

        Contains(bytes, EscPos.Gs, (byte)'k', 73).Should().BeTrue("Code 128 is GS k 73");
        Contains(bytes, EscPos.Gs, (byte)'H', 2).Should().BeTrue("the digits print under the bars");
        EscPosDump.Describe(bytes).Should().Contain("\"{BINV-2026-004312\"", "code set B is selected");
    }

    [Fact]
    public void PRT_04_ThePrinterCanBeSwitchedToRasterBarcodesWithoutTouchingTheDocument()
    {
        var document = ReceiptDocument.Of(new ReceiptNode.Barcode("INV-2026-004312"));
        var capabilities = PrinterCapabilities.Default with { BarcodeMode = BarcodeMode.Raster };

        var bytes = new EscPosRenderer(capabilities, new StubRasteriser()).Render(document);

        Contains(bytes, EscPos.Gs, (byte)'k').Should().BeFalse("GS k is exactly what the fallback avoids");
        Contains(bytes, EscPos.Gs, (byte)'v', (byte)'0').Should().BeTrue("the bars go out as a raster");
    }

    [Fact]
    public void PRT_04_RasterBarcodesWithoutARasteriserAreRefusedWhenTheRendererIsWired()
    {
        var capabilities = PrinterCapabilities.Default with { BarcodeMode = BarcodeMode.Raster };

        var wiring = () => new EscPosRenderer(capabilities);

        wiring.Should().Throw<ArgumentException>(
            "a misconfiguration must surface at start-up, not halfway through a bill");
    }

    [Fact]
    public void PRT_04_TheIrCarriesARasterBarcodeNodeEvenThoughGsKIsTheDefault()
    {
        var image = StubRasteriser.SolidBlock(widthDots: 16, heightDots: 2);

        var bytes = Render(new ReceiptNode.RasterBarcode(image, "INV-2026-004312"));

        Contains(bytes, EscPos.Gs, (byte)'v', (byte)'0', 0, 2, 0, 2, 0).Should().BeTrue(
            "GS v 0 with two bytes per row and two rows");
        Contains(bytes, EscPos.Gs, (byte)'k').Should().BeFalse();
    }

    [Fact]
    public void PRT_04_AQrCodeRendersAsTheGsParenKSequence()
    {
        var bytes = Render(new ReceiptNode.QrCode("INV-2026-004312"));

        var dump = EscPosDump.Describe(bytes);

        dump.Should().Contain("QR select model");
        dump.Should().Contain("QR store \"INV-2026-004312\"");
        dump.Should().Contain("QR print");
    }

    [Fact]
    public void ACodePageIsSelectedOnlyWhenThePrinterUnderstandsTheCommand()
    {
        Contains(Render(new ReceiptNode.TextLine("x")), EscPos.Esc, (byte)'t', 0)
            .Should().BeTrue();

        var quirky = new EscPosRenderer(PrinterCapabilities.Default with { SupportsCodePage = false })
            .Render(ReceiptDocument.Of(new ReceiptNode.TextLine("x")));

        Contains(quirky, EscPos.Esc, (byte)'t').Should().BeFalse();
    }

    [Fact]
    public void AlignmentFallsBackToPaddingWhenThePrinterHasNoEscA()
    {
        var capabilities = PrinterCapabilities.Default with { SupportsAlign = false };

        var bytes = new EscPosRenderer(capabilities).Render(
            ReceiptDocument.Of(new ReceiptNode.TextLine("SHOP NAME", TextAlign.Centre)));

        Contains(bytes, EscPos.Esc, (byte)'a').Should().BeFalse();
        EscPosDump.PrintedLines(bytes).Should().ContainSingle()
            .Which.Should().Be(new string(' ', (EightyMillimetreColumns - "SHOP NAME".Length) / 2) + "SHOP NAME");
    }

    [Fact]
    public void AlignmentIsOnlySentWhenItChanges()
    {
        var bytes = Render(
            new ReceiptNode.TextLine("centred", TextAlign.Centre),
            new ReceiptNode.TextLine("still centred", TextAlign.Centre),
            new ReceiptNode.TextLine("left again"));

        CountOf(bytes, EscPos.Esc, (byte)'a').Should().Be(2, "centre, then back to left");
    }

    [Fact]
    public void CharactersThePrinterCannotRenderBecomeQuestionMarksRatherThanNoise()
    {
        var lines = EscPosDump.PrintedLines(Render(new ReceiptNode.TextLine("Caf\u00e9 \u20ac5")));

        lines.Should().ContainSingle().Which.Should().Be("Caf? ?5");
    }

    private static byte[] Render(params ReceiptNode[] nodes) =>
        new EscPosRenderer().Render(ReceiptDocument.Of(nodes));

    private static bool Contains(byte[] haystack, params byte[] needle) =>
        IndexesOf(haystack, needle).Any();

    private static int CountOf(byte[] haystack, params byte[] needle) =>
        IndexesOf(haystack, needle).Count();

    private static IEnumerable<int> IndexesOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                yield return i;
            }
        }
    }

    /// <summary>
    /// Stands in for the ZXing-backed rasteriser <c>HW-T01</c> would wire up. It draws a solid
    /// block, which is enough to prove the renderer takes the raster path.
    /// </summary>
    private sealed class StubRasteriser : IBarcodeRasteriser
    {
        public RasterImage RasteriseBarcode(
            string data,
            BarcodeSymbology symbology,
            int maxWidthDots,
            int heightDots) => SolidBlock(maxWidthDots, heightDots);

        public RasterImage RasteriseQrCode(string data, int maxWidthDots) =>
            SolidBlock(maxWidthDots, maxWidthDots);

        public static RasterImage SolidBlock(int widthDots, int heightDots)
        {
            var bytesPerRow = (widthDots + 7) / 8;
            var bits = new byte[bytesPerRow * heightDots];

            Array.Fill(bits, (byte)0xFF);

            return new RasterImage(widthDots, heightDots, bits);
        }
    }
}
