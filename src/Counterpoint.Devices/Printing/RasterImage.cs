using System;
using System.Globalization;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// A 1-bit-per-pixel bitmap, in the exact layout <c>GS v 0</c> wants: rows top to bottom,
/// each row <c>ceil(width / 8)</c> bytes, most significant bit leftmost, a set bit meaning a
/// black dot.
///
/// Everything that cannot be printed as characters arrives here: a shop logo, a barcode on a
/// printer whose <c>GS k</c> misbehaves, and - if a second language is ever switched on
/// (Q-C) - text shaped into a bitmap, because a thermal printer has no font for Sinhala or
/// Tamil.
/// </summary>
public sealed class RasterImage
{
    /// <summary>
    /// Wraps an already-packed bitmap.
    /// </summary>
    /// <param name="widthDots">Width in dots. 576 is the full width of 80 mm paper at 203 dpi.</param>
    /// <param name="heightDots">Height in dots.</param>
    /// <param name="bits">
    /// <c>BytesPerRow * heightDots</c> bytes of packed pixels.
    /// </param>
    public RasterImage(int widthDots, int heightDots, ReadOnlyMemory<byte> bits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(widthDots, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(heightDots, 1);

        var bytesPerRow = (widthDots + 7) / 8;
        var expected = bytesPerRow * heightDots;

        if (bits.Length != expected)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {widthDots} x {heightDots} raster needs exactly {expected} bytes "
                    + $"({bytesPerRow} per row), not {bits.Length}."),
                nameof(bits));
        }

        WidthDots = widthDots;
        HeightDots = heightDots;
        Bits = bits;
    }

    /// <summary>Width in dots.</summary>
    public int WidthDots { get; }

    /// <summary>Height in dots.</summary>
    public int HeightDots { get; }

    /// <summary>Packed pixels, row major.</summary>
    public ReadOnlyMemory<byte> Bits { get; }

    /// <summary>Bytes each row occupies - the <c>xL xH</c> of <c>GS v 0</c>.</summary>
    public int BytesPerRow => (WidthDots + 7) / 8;
}
