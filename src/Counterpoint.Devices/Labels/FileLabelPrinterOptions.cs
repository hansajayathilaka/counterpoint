using System;
using System.IO;
using Counterpoint.Devices.Printing;

namespace Counterpoint.Devices.Labels;

/// <summary>
/// How <see cref="FileLabelPrinter"/> behaves: where the byte streams land, and whether it
/// pretends to be broken.
/// </summary>
public sealed record FileLabelPrinterOptions
{
    /// <summary>
    /// Folder the <c>.bin</c> files are written to. Defaults to
    /// <see cref="DefaultOutputDirectory"/>; created on first use.
    /// </summary>
    public string OutputDirectory { get; init; } = DefaultOutputDirectory;

    /// <summary>
    /// Failure injection, shared with <c>FileReceiptPrinter</c> - the same plug for the same rule
    /// (SRS FR-7.8, AC-16 applied to a label print). <see cref="PrinterFailureMode.None"/> by
    /// default.
    /// </summary>
    public PrinterFailureMode FailureMode { get; init; } = PrinterFailureMode.None;

    /// <summary>
    /// Clock used to stamp file names. Injectable so a test can produce a predictable name.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// <c>artifacts/labels</c> beside the running binary - the label-printing counterpart of
    /// <c>FileReceiptPrinterOptions.DefaultOutputDirectory</c>, per CLAUDE.md's development
    /// platform note.
    /// </summary>
    public static string DefaultOutputDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "artifacts", "labels");
}
