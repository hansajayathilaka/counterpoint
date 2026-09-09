using System;
using Counterpoint.Devices.Printing;
using FluentAssertions;
using ZXing;
using ZXing.Common;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The ZXing raster fallback, round-tripped: encode the bill number, decode the exact bitmap
/// back, and the text that comes out must be the bill number that went in (SRS FR-7.4, PRT-04).
/// Scanning the printed paper is <c>HW-T01</c>'s job; this is what a byte-stream test on Linux
/// can prove.
/// </summary>
public sealed class ZXingBarcodeRasteriserTests
{
    private const string BillNumber = "INV-2026-004312";

    [Fact]
    public void FR_7_4_TheQrRasterDecodesBackToTheExactBillNumber()
    {
        var rasteriser = new ZXingBarcodeRasteriser();

        var image = rasteriser.RasteriseQrCode(BillNumber, maxWidthDots: 200);

        Decode(image).Should().Be(BillNumber);
    }

    [Fact]
    public void FR_7_4_TheCode128BarcodeRasterDecodesBackToTheExactBillNumber()
    {
        var rasteriser = new ZXingBarcodeRasteriser();

        var image = rasteriser.RasteriseBarcode(BillNumber, BarcodeSymbology.Code128, maxWidthDots: 400, heightDots: 80);

        Decode(image).Should().Be(BillNumber);
    }

    [Fact]
    public void FR_7_4_TheWholeEscPosPipelineProducesAQrThatDecodesToTheBillNumber()
    {
        var capabilities = PrinterCapabilities.Default with { BarcodeMode = BarcodeMode.Raster };
        var renderer = new EscPosRenderer(capabilities, new ZXingBarcodeRasteriser());

        var bytes = renderer.Render(ReceiptDocument.Of(new ReceiptNode.QrCode(BillNumber)));

        var image = ExtractRaster(bytes);
        Decode(image).Should().Be(BillNumber, "the raster the printer actually receives must decode to the bill number");
    }

    /// <summary>Decodes a raster back to text, the same way a real scanner would from the paper.</summary>
    private static string Decode(RasterImage image)
    {
        var rgb = ToRgbBytes(image);
        var source = new RGBLuminanceSource(rgb, image.WidthDots, image.HeightDots);
        var bitmap = new BinaryBitmap(new HybridBinarizer(source));

        var result = new MultiFormatReader().decode(bitmap)
            ?? throw new InvalidOperationException("The rasterised image did not decode to anything.");

        return result.Text;
    }

    /// <summary>Unpacks the 1-bpp <c>GS v 0</c> layout into plain RGB triples ZXing can read.</summary>
    private static byte[] ToRgbBytes(RasterImage image)
    {
        var bits = image.Bits.Span;
        var bytesPerRow = image.BytesPerRow;
        var rgb = new byte[image.WidthDots * image.HeightDots * 3];
        var offset = 0;

        for (var y = 0; y < image.HeightDots; y++)
        {
            for (var x = 0; x < image.WidthDots; x++)
            {
                var byteIndex = (y * bytesPerRow) + (x / 8);
                var bitMask = (byte)(0x80 >> (x % 8));
                var isBlack = (bits[byteIndex] & bitMask) != 0;
                var value = (byte)(isBlack ? 0 : 255);

                rgb[offset] = value;
                rgb[offset + 1] = value;
                rgb[offset + 2] = value;
                offset += 3;
            }
        }

        return rgb;
    }

    /// <summary>Finds <c>GS v 0</c> in a rendered stream and rebuilds the <see cref="RasterImage"/> it carries.</summary>
    private static RasterImage ExtractRaster(byte[] stream)
    {
        // GS v 0 m xL xH yL yH
        ReadOnlySpan<byte> marker = [0x1D, (byte)'v', (byte)'0', 0];

        var index = FindSequence(stream, marker);
        index.Should().BeGreaterThan(-1, "the QR code must reach the byte stream as a GS v 0 raster");

        var xL = stream[index + 4];
        var xH = stream[index + 5];
        var yL = stream[index + 6];
        var yH = stream[index + 7];

        var bytesPerRow = xL | (xH << 8);
        var heightDots = yL | (yH << 8);
        var widthDots = bytesPerRow * 8;

        var bits = new byte[bytesPerRow * heightDots];
        Array.Copy(stream, index + 8, bits, 0, bits.Length);

        return new RasterImage(widthDots, heightDots, bits);
    }

    private static int FindSequence(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            var match = true;
            for (var i = 0; i < needle.Length; i++)
            {
                if (haystack[start + i] != needle[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return start;
            }
        }

        return -1;
    }
}
