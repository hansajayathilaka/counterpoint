using System;
using System.Linq;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The bridge from a committed bill to a byte stream (SRS FR-7.1, FR-7.4, FR-7.7).
/// </summary>
/// <remarks>
/// The layout itself is not pinned here. It is a placeholder until the owner-editable template
/// arrives in P1-T11, and a snapshot of a layout nobody has agreed to would only have to be
/// re-approved. What is asserted is what the device has to get right whatever the layout says:
/// the bill number reaches the paper and the barcode, and the drawer opens for cash and only
/// for cash.
/// </remarks>
public sealed class EscPosSaleReceiptRendererTests
{
    private const long Pieces = 1;

    [Fact]
    public void FR_7_1_TheBillNumberAndTheTotalReachThePaper()
    {
        var bytes = Render(Receipt("CASH"));

        var lines = EscPosDump.PrintedLines(bytes);

        lines.Should().Contain(line => line.Contains("INV-2026-000001", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Galvanised bolt M8", StringComparison.Ordinal));
        lines.Should().Contain(line => line.TrimEnd().EndsWith("25.00", StringComparison.Ordinal));
    }

    [Fact]
    public void FR_7_4_TheBillNumberIsAlsoEncodedAsABarcodeSoAReturnCanScanTheReceipt()
    {
        var bytes = Render(Receipt("CASH"));

        // GS k 73 (Code 128), then the length-prefixed data.
        var barcode = FindSequence(bytes, [0x1D, 0x6B, 73]);

        barcode.Should().BeGreaterThan(-1, "the bill number prints as a Code 128 symbol");
    }

    [Fact]
    public void FR_7_7_TheDrawerOpensForCashAndOnlyForCash()
    {
        // ESC p 0 25 250
        var kick = new byte[] { 0x1B, 0x70, 0x00, 25, 250 };

        FindSequence(Render(Receipt("CASH")), kick)
            .Should().BeGreaterThan(-1, "a cash tender opens the drawer");

        FindSequence(Render(Receipt("CARD")), kick)
            .Should().Be(-1, "a card sale that popped the drawer would be a reconciliation problem");
    }

    [Fact]
    public void PRT_05_TheReceiptEndsWithACut()
    {
        var bytes = Render(Receipt("CASH"));

        // GS V 1 - partial cut, the default capability (PrinterCapabilities.CutMode).
        FindSequence(bytes, [0x1D, 0x56, 1]).Should().BeGreaterThan(-1);
    }

    private static byte[] Render(SaleReceipt receipt) =>
        new EscPosSaleReceiptRenderer(new EscPosRenderer(), new HalfAwayFromZeroRounding(decimalPlaces: 2))
            .Render(receipt);

    private static SaleReceipt Receipt(string tenderType) => new(
        "INV-2026-000001",
        new DateTimeOffset(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5)),
        [
            new SaleReceiptLine(
                "Galvanised bolt M8",
                Quantity.FromDecimal(2m, Pieces),
                "pc",
                Money.FromDecimal(12.50m),
                Money.FromDecimal(25.00m)),
        ],
        Money.FromDecimal(25.00m),
        Money.Zero,
        Money.FromDecimal(25.00m),
        [new SaleReceiptTender(tenderType, Money.FromDecimal(25.00m))]);

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
