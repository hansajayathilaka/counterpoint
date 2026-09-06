namespace Counterpoint.Devices.Printing;

/// <summary>
/// Where the printer prints the human-readable interpretation - the digits under a barcode.
/// The values are the <c>n</c> of <c>GS H n</c>.
/// </summary>
public enum HriPosition
{
    /// <summary>Not printed.</summary>
    None = 0,

    /// <summary>Above the barcode.</summary>
    Above = 1,

    /// <summary>
    /// Below the barcode. The default: the SRS §10.1 specimen shows the bill number under
    /// its barcode, and a cashier reading it out is the fallback when a scan fails.
    /// </summary>
    Below = 2,

    /// <summary>Both above and below.</summary>
    AboveAndBelow = 3,
}
