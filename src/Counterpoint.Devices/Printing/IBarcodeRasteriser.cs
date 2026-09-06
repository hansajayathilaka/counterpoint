namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a barcode or QR code into a bitmap, for printers whose native <c>GS k</c> or
/// <c>GS ( k</c> cannot be trusted (SRS PRT-04).
///
/// There is deliberately no implementation yet. The default is
/// <see cref="BarcodeMode.Native"/>, and the seam exists so that <c>HW-T01</c> can drop in a
/// ZXing-backed rasteriser and flip one capability flag, without the renderer or any caller
/// changing.
/// </summary>
public interface IBarcodeRasteriser
{
    /// <summary>Renders a 1D barcode.</summary>
    /// <param name="data">The value to encode - typically the bill number.</param>
    /// <param name="symbology">Which symbology to encode it in.</param>
    /// <param name="maxWidthDots">The printable width, in dots. The result may be narrower.</param>
    /// <param name="heightDots">Bar height, in dots.</param>
    public RasterImage RasteriseBarcode(
        string data,
        BarcodeSymbology symbology,
        int maxWidthDots,
        int heightDots);

    /// <summary>Renders a QR code.</summary>
    /// <param name="data">The value to encode.</param>
    /// <param name="maxWidthDots">The printable width, in dots. The result may be narrower.</param>
    public RasterImage RasteriseQrCode(string data, int maxWidthDots);
}
