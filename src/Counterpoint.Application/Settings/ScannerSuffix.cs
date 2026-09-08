namespace Counterpoint.Application.Settings;

/// <summary>
/// What the barcode scanner sends after the code itself (SRS FR-10.6). The scanner is a keyboard
/// wedge, so this is what the till watches for to know a scan has finished.
/// </summary>
public enum ScannerSuffix
{
    /// <summary>Carriage return / Enter. The usual factory setting.</summary>
    Enter = 0,

    /// <summary>Tab.</summary>
    Tab = 1,

    /// <summary>Nothing; the till decides a scan is over from the inter-character timing.</summary>
    None = 2,
}
