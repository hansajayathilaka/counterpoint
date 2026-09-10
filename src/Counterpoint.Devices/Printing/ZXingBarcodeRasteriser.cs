using System;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Renders a barcode or QR code to a bitmap using ZXing.Net's pure-managed encoder - the fallback
/// for a printer whose native <c>GS k</c> or <c>GS ( k</c> cannot be trusted (SRS FR-7.4, PRT-04).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately no <c>System.Drawing</c>: <see cref="MultiFormatWriter"/> produces a
/// <see cref="BitMatrix"/> - modules on or off - with no bitmap library underneath it, which is
/// what lets this build and run on the Linux dev/CI track exactly as every other device fake does
/// (CLAUDE.md "Development platform note").
/// </para>
/// <para>
/// Which mode a printer uses is a capability decided once at setup
/// (<see cref="PrinterCapabilities.BarcodeMode"/>), never probed per receipt - this class only
/// answers "draw it", it never decides whether to.
/// </para>
/// </remarks>
public sealed class ZXingBarcodeRasteriser : IBarcodeRasteriser
{
    /// <inheritdoc />
    public RasterImage RasteriseBarcode(string data, BarcodeSymbology symbology, int maxWidthDots, int heightDots)
    {
        ArgumentException.ThrowIfNullOrEmpty(data);

        var format = ToBarcodeFormat(symbology);
        var writer = new MultiFormatWriter();
        var hints = new EncodingOptions { Width = maxWidthDots, Height = heightDots, Margin = 0 };

        var matrix = writer.encode(data, format, maxWidthDots, heightDots, hints.Hints);
        return ToRasterImage(matrix);
    }

    /// <inheritdoc />
    public RasterImage RasteriseQrCode(string data, int maxWidthDots)
    {
        ArgumentException.ThrowIfNullOrEmpty(data);

        var writer = new QRCodeWriter();
        var hints = new EncodingOptions { Width = maxWidthDots, Height = maxWidthDots, Margin = 0 };

        var matrix = writer.encode(data, BarcodeFormat.QR_CODE, maxWidthDots, maxWidthDots, hints.Hints);
        return ToRasterImage(matrix);
    }

    /// <summary>Packs a ZXing module matrix into the 1-bpp layout <c>GS v 0</c> wants.</summary>
    private static RasterImage ToRasterImage(BitMatrix matrix)
    {
        var width = matrix.Width;
        var height = matrix.Height;
        var bytesPerRow = (width + 7) / 8;
        var bits = new byte[bytesPerRow * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!matrix[x, y])
                {
                    continue;
                }

                var byteIndex = (y * bytesPerRow) + (x / 8);
                var bitMask = (byte)(0x80 >> (x % 8));
                bits[byteIndex] |= bitMask;
            }
        }

        return new RasterImage(width, height, bits);
    }

    private static BarcodeFormat ToBarcodeFormat(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.UpcA => BarcodeFormat.UPC_A,
        BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
        BarcodeSymbology.Ean8 => BarcodeFormat.EAN_8,
        BarcodeSymbology.Code39 => BarcodeFormat.CODE_39,
        BarcodeSymbology.Interleaved2Of5 => BarcodeFormat.ITF,
        BarcodeSymbology.Code128 => BarcodeFormat.CODE_128,
        _ => throw new ArgumentOutOfRangeException(nameof(symbology), symbology, "Unknown barcode symbology."),
    };
}
