using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
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
}
