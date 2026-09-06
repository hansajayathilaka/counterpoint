using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VerifyXunit;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The SRS §10.1 specimen bill, rendered and locked down (SRS FR-7.1, §10).
///
/// The snapshot is the contract: if a change to the renderer moves a single byte of this
/// receipt, the diff shows both the commands and the paper. <c>HW-T01</c> prints exactly
/// these bytes on the shop's printer and checks the alignment, the cut and the drawer against
/// the same listing.
/// </summary>
public sealed class SpecimenReceiptTests : IDisposable
{
    /// <summary>The money column on 80 mm paper, in characters.</summary>
    private const int MoneyColumnWidth = 11;

    private readonly string _outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "counterpoint-specimen-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Fact]
    public Task Srs_10_1_TheSpecimenBillRendersToTheCommittedByteStream()
    {
        var bytes = new EscPosRenderer().Render(SpecimenReceipt.Build());

        return Verifier.Verify(EscPosDump.Describe(bytes)).UseDirectory("Snapshots");
    }

    [Fact]
    public void Srs_10_1_TheSpecimenFitsThePaperAndLinesUpItsAmounts()
    {
        var bytes = new EscPosRenderer().Render(SpecimenReceipt.Build());

        var lines = EscPosDump.PrintedLines(bytes);

        lines.Should().AllSatisfy(
            line => line.Length.Should().BeLessThanOrEqualTo(48, "80 mm paper is 48 characters"));

        lines.Should().Contain(
            line => line.EndsWith("     500.00", StringComparison.Ordinal),
            "20 pcs at 25.00 is 500.00, right-aligned in the money column");
        lines.Should().Contain(
            line => line.EndsWith("    1155.00", StringComparison.Ordinal),
            "2.75 m at 420.00 is 1155.00 - a fractional quantity, exact in decimal");
        lines.Should().Contain(
            line => line.Contains("(cut to length - non returnable)", StringComparison.Ordinal),
            "a non-returnable line is annotated on the bill (SRS PRT-06)");

        var amountColumn = lines
            .Where(line => line.EndsWith(".00", StringComparison.Ordinal))
            .Select(line => line.Length)
            .Distinct();

        amountColumn.Should().HaveCountLessThanOrEqualTo(
            2,
            "amounts end at the right edge - 48 characters, or 24 on the double-width total");
    }

    [Fact]
    public void Invariant_2_TheSpecimenRoundsAtTheLineTotalAndTheBillTotalAndNowhereElse()
    {
        // A rounding policy nobody could miss: whatever it touches comes back as zero. Every
        // amount that still prints its own value is an amount that was never rounded.
        var lines = EscPosDump.PrintedLines(
            new EscPosRenderer().Render(SpecimenReceipt.Build(new ZeroingRounding())));

        Amount(lines, "Sub total").Should().Be(
            "0.00",
            "it is the sum of the line totals, and the line total is one of the two rounding points");
        Amount(lines, "TOTAL").Should().Be("0.00", "the bill total is the other");

        Amount(lines, "Discount").Should().Be(
            "-65.00",
            "the discount is not a rounding point - if formatting rounded, this would be 0.00");
        Amount(lines, "Cash").Should().Be("2500.00", "nor is the cash tendered");
        Amount(lines, "CHANGE").Should().Be("2500.00", "nor the change given back");
    }

    [Fact]
    public async Task Srs_10_1_TheSpecimenPrintsThroughTheFileReceiptPrinter()
    {
        var bytes = new EscPosRenderer().Render(SpecimenReceipt.Build());
        var printer = new FileReceiptPrinter(
            new FileReceiptPrinterOptions { OutputDirectory = _outputDirectory },
            NullLogger<FileReceiptPrinter>.Instance);

        var outcome = await printer.PrintAsync(bytes, SpecimenReceipt.BillNumber);

        outcome.Succeeded.Should().BeTrue();
        outcome.Target.Should().NotBeNull();
        (await File.ReadAllBytesAsync(outcome.Target!)).Should().Equal(
            bytes,
            "what the printer receives is exactly what the renderer produced");
    }

    /// <summary>The amount printed in the money column of the row carrying a given label.</summary>
    private static string Amount(List<string> lines, string label) =>
        lines.Single(line => line.StartsWith(label + " ", StringComparison.Ordinal))
            [^MoneyColumnWidth..]
            .Trim();

    /// <summary>
    /// A rounding policy that returns zero for everything. Not a plausible shop setting - a
    /// dye: every amount that still prints its own value went nowhere near it.
    /// </summary>
    private sealed class ZeroingRounding : IRoundingPolicy
    {
        public int DecimalPlaces => 2;

        public Money Round(Money amount) => Money.Zero;
    }
}
