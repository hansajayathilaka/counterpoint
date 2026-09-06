using System;
using System.IO;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// How <see cref="FileReceiptPrinter"/> behaves: where the byte streams land, and whether it
/// pretends to be broken.
/// </summary>
public sealed record FileReceiptPrinterOptions
{
    /// <summary>
    /// Folder the <c>.bin</c> files are written to. Defaults to
    /// <see cref="DefaultOutputDirectory"/>; created on first use.
    /// </summary>
    public string OutputDirectory { get; init; } = DefaultOutputDirectory;

    /// <summary>Failure injection. <see cref="PrinterFailureMode.None"/> by default.</summary>
    public PrinterFailureMode FailureMode { get; init; } = PrinterFailureMode.None;

    /// <summary>
    /// Clock used to stamp file names. Injectable so a test can produce a predictable name.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// <c>artifacts/receipts</c> beside the running binary - the convention in CLAUDE.md's
    /// development platform note.
    /// </summary>
    public static string DefaultOutputDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "artifacts", "receipts");
}
