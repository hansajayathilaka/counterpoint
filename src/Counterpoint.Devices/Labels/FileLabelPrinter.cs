using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Devices.Printing;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Devices.Labels;

/// <summary>
/// The development and CI label printer: it writes the rendered TSPL byte stream to
/// <c>artifacts/labels/*.bin</c> instead of to a printer.
///
/// <para>
/// This is the only <see cref="ILabelPrinter"/> the software track needs. Every phase-1 to
/// phase-5 label print goes through it, on Linux, byte for byte; the Windows raw spooler
/// implementation and every physical check belong to <c>HW-T03</c>. The files it leaves behind
/// are also the artefact a developer inspects when a label layout looks wrong.
/// </para>
/// <para>
/// It never throws for a printing problem (CLAUDE.md invariant 7), the same rule
/// <see cref="FileReceiptPrinter"/> follows. A full disk, a read-only folder, or
/// <see cref="PrinterFailureMode.FailEveryJob"/> all come back as a failed
/// <see cref="PrintOutcome"/> with a warning in the log.
/// </para>
/// </summary>
public sealed partial class FileLabelPrinter : ILabelPrinter
{
    /// <summary>Stands in for the file name in a log line written before there was one.</summary>
    private const string UnknownPath = "(no output path)";

    private readonly FileLabelPrinterOptions _options;
    private readonly ILogger<FileLabelPrinter> _logger;

    /// <summary>Creates the printer.</summary>
    /// <param name="options">Where to write, and whether to fail on purpose.</param>
    /// <param name="logger">Where the degradation warning goes.</param>
    public FileLabelPrinter(FileLabelPrinterOptions options, ILogger<FileLabelPrinter> logger)
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

        // Only a placeholder until the path is known: everything that can fail, including
        // working out where to write, happens inside the try.
        var path = UnknownPath;

        try
        {
            if (string.IsNullOrWhiteSpace(_options.OutputDirectory))
            {
                // Misconfiguration, but still only a printing problem.
                throw new IOException(
                    "FileLabelPrinterOptions.OutputDirectory is not set, so there is nowhere to "
                    + "write the labels.");
            }

            path = Path.Combine(_options.OutputDirectory, FileNameFor(jobName));

            if (_options.FailureMode == PrinterFailureMode.FailEveryJob)
            {
                throw new IOException(
                    "Simulated printer failure (FileLabelPrinterOptions.FailureMode).");
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
                // A path the file system rejects - an embedded null, an empty component. Not
                // ArgumentNullException or ArgumentOutOfRangeException: those are programming
                // bugs, and telling the owner the printer is unwell would hide them.
                || (ex is ArgumentException
                    and not (ArgumentNullException or ArgumentOutOfRangeException)))
        {
            // Warned about, not thrown: nothing about printing labels may escape to the caller as
            // an exception (CLAUDE.md invariant 7).
            PrintFailed(jobName, path, ex);

            return PrintOutcome.Failed(
                "The labels could not be printed. Try again once the printer is back.");
        }
    }

    /// <summary>
    /// A sortable, human-readable file name: when it printed, and which batch it was.
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
        EventId = 7201,
        Level = LogLevel.Information,
        Message = "Labels {JobName} ({ByteCount} bytes) written to {Path}.")]
    private partial void PrintedJob(string jobName, int byteCount, string path);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Warning,
        Message = "Labels {JobName} could not be written to {Path}.")]
    private partial void PrintFailed(string jobName, string path, Exception exception);
}
