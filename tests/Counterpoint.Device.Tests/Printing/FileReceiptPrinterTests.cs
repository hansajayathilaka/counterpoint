using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The development printer, and the rule it exists to make testable: a printer failure warns
/// and is written down, it never throws (SRS FR-7.8, AC-16, CLAUDE.md invariant 7).
/// </summary>
public sealed class FileReceiptPrinterTests : IDisposable
{
    private static readonly byte[] Document = [0x1B, 0x40, (byte)'H', (byte)'i', 0x0A];

    private readonly string _outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "counterpoint-receipts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void TheDefaultOutputDirectoryIsArtifactsReceipts()
    {
        FileReceiptPrinterOptions.DefaultOutputDirectory.Should().EndWith(
            Path.Combine("artifacts", "receipts"),
            "CLAUDE.md's development platform note names this path");
    }

    [Fact]
    public async Task TheByteStreamIsWrittenVerbatim()
    {
        var logger = new RecordingLogger<FileReceiptPrinter>();
        var printer = new FileReceiptPrinter(
            new FileReceiptPrinterOptions { OutputDirectory = _outputDirectory },
            logger);

        var outcome = await printer.PrintAsync(Document, "INV-2026-004312");

        outcome.Succeeded.Should().BeTrue();
        outcome.FailureReason.Should().BeNull();

        var written = Directory.GetFiles(_outputDirectory, "*.bin");
        written.Should().ContainSingle().Which.Should().Be(outcome.Target);
        (await File.ReadAllBytesAsync(written[0])).Should().Equal(Document);
        Path.GetFileName(written[0]).Should().EndWith(
            "-INV-2026-004312.bin",
            "the file names the document, so a developer can find the bill they are looking at");
    }

    [Fact]
    public async Task FR_7_8_AConfiguredFailureIsCaughtAndLoggedNotThrown()
    {
        var logger = new RecordingLogger<FileReceiptPrinter>();
        var printer = new FileReceiptPrinter(
            new FileReceiptPrinterOptions
            {
                OutputDirectory = _outputDirectory,
                FailureMode = PrinterFailureMode.FailEveryJob,
            },
            logger);

        var print = async () => await printer.PrintAsync(Document, "INV-2026-004312");

        var outcome = (await print.Should().NotThrowAsync(
            "a printer failure must never reach the sale that queued the job")).Subject;

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().Contain(
            "The bill is saved",
            "the cashier is told what to do next, in plain language (SRS UI-06)");

        var warning = logger.Entries.Should().ContainSingle().Which;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Exception.Should().NotBeNull();
        Directory.Exists(_outputDirectory).Should().BeFalse("nothing was written");
    }

    [Fact]
    public async Task FR_7_8_AnUnusableOutputFolderIsCaughtAndLoggedToo()
    {
        // A file where the folder should be: the same shape as a printer that has gone away.
        Directory.CreateDirectory(_outputDirectory);
        var blocked = Path.Combine(_outputDirectory, "blocked");
        await File.WriteAllTextAsync(blocked, "not a directory");

        var logger = new RecordingLogger<FileReceiptPrinter>();
        var printer = new FileReceiptPrinter(
            new FileReceiptPrinterOptions { OutputDirectory = blocked },
            logger);

        var outcome = await printer.PrintAsync(Document, "INV-2026-004312");

        outcome.Succeeded.Should().BeFalse();
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task ASuccessfulPrintIsLoggedAtInformation()
    {
        var logger = new RecordingLogger<FileReceiptPrinter>();
        var printer = new FileReceiptPrinter(
            new FileReceiptPrinterOptions { OutputDirectory = _outputDirectory },
            logger);

        await printer.PrintAsync(Document, "INV-2026-004312");

        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
        logger.Entries.Single().Message.Should().Contain("INV-2026-004312");
    }
}
