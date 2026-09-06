namespace Counterpoint.Devices.Printing;

/// <summary>
/// How a <see cref="ReceiptNode.Barcode"/> is put on paper. A capability, not a per-receipt
/// choice: it is decided once at setup and stored in settings, never probed at print time.
/// </summary>
public enum BarcodeMode
{
    /// <summary>
    /// The printer's own <c>GS k</c> command. The default - it is compact, fast, and every
    /// printer that claims ESC/POS claims this too.
    /// </summary>
    Native = 0,

    /// <summary>
    /// Render the barcode to a bitmap and send it as a <c>GS v 0</c> raster. Slower, but it
    /// works on a printer whose <c>GS k</c> is wrong or missing. Selecting this requires an
    /// <see cref="IBarcodeRasteriser"/>; wiring one is <c>HW-T01</c>'s job if the shop's
    /// printer needs it.
    /// </summary>
    Raster = 1,
}
