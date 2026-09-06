namespace Counterpoint.Devices.Printing;

/// <summary>
/// Deliberate failure injection for <see cref="FileReceiptPrinter"/>.
///
/// The rule that a printer failure never blocks a sale (SRS FR-7.8, AC-16) is only worth
/// anything if it is tested, and on Linux there is no printer to unplug. This is the plug.
/// The equivalent test with a physically disconnected printer is <c>HW-T01</c>.
/// </summary>
public enum PrinterFailureMode
{
    /// <summary>Print normally. The default.</summary>
    None = 0,

    /// <summary>Fail every job, as an unreachable or out-of-paper printer would.</summary>
    FailEveryJob = 1,
}
