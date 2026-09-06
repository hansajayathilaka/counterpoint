using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// The development and CI receipt printer: it writes the rendered ESC/POS byte stream to
/// <c>artifacts/receipts/*.bin</c> instead of to a printer.
///
/// <para>
/// This is the only <see cref="IReceiptPrinter"/> the software track needs. Every phase-0 to
/// phase-5 acceptance test prints through it, on Linux, byte for byte; the Windows raw
/// spooler implementation and every physical check belong to <c>HW-T01</c>. The files it
/// leaves behind are also the artefact a developer inspects when a layout looks wrong.
/// </para>
///
/// <para>
/// It never throws for a printing problem (SRS FR-7.8, AC-16). A full disk, a read-only
/// folder, or <see cref="PrinterFailureMode.FailEveryJob"/> all come back as a failed
/// <see cref="PrintOutcome"/> with a warning in the log. The sale is already committed by the
/// time anything calls this, and nothing here may undo it.
/// </para>
/// </summary>
public sealed partial class FileReceiptPrinter : IReceiptPrinter
{
    private readonly FileReceiptPrinterOptions _options;
    private readonly ILogger<FileReceiptPrinter> _logger;

    /// <summary>Creates the printer.</summary>
    /// <param name="options">Where to write, and whether to fail on purpose.</param>
    /// <param name="logger">Where the degradation warning goes.</param>
    public FileReceiptPrinter(FileReceiptPrinterOptions options, ILogger<FileReceiptPrinter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PrintOutcome> PrintAsync(
        ReadOnlyMemory<byte> document,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        var path = Path.Combine(_options.OutputDirectory, FileNameFor(jobName));

        try
        {
            if (_options.FailureMode == PrinterFailureMode.FailEveryJob)
            {
                throw new IOException(
                    "Simulated printer failure (FileReceiptPrinterOptions.FailureMode).");
            }

            Directory.CreateDirectory(_options.OutputDirectory);
            await File.WriteAllBytesAsync(path, document, cancellationToken).ConfigureAwait(false);

            PrintedJob(jobName, document.Length, path);

            return PrintOutcome.Success(path);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            // Warned about, not thrown: the bill is already saved and the cashier is mid-queue.
            PrintFailed(jobName, path, ex);

            return PrintOutcome.Failed(
                "The receipt could not be written. The bill is saved - reprint it when the "
                + "printer is back.");
        }
    }

    /// <summary>
    /// A sortable, human-readable file name: when it printed, and which document it was.
    /// </summary>
    private string FileNameFor(string jobName)
    {
        var stamp = _options.TimeProvider.GetUtcNow()
            .ToString("yyyyMMdd'-'HHmmss'-'fff", CultureInfo.InvariantCulture);

        var safe = new string(
            jobName.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c)
                .ToArray());

        return string.Create(CultureInfo.InvariantCulture, $"{stamp}-{safe}.bin");
    }

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Information,
        Message = "Receipt {JobName} ({ByteCount} bytes) written to {Path}.")]
    private partial void PrintedJob(string jobName, int byteCount, string path);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Warning,
        Message = "Receipt {JobName} could not be written to {Path}. The bill is unaffected.")]
    private partial void PrintFailed(string jobName, string path, Exception exception);
}
