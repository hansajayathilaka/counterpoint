using System;
using System.Globalization;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// The ESC/POS command set, as bytes and nothing else (SRS FR-7.1).
///
/// Every command the renderer emits is built here, one method per command, so that when the
/// shop's printer turns out to disagree with the standard - and it will, on the cutter, the
/// code page or the barcode - the fix is a capability flag plus at most one method in this
/// file. <c>HW-T01</c> records what it found in <c>docs/adr/printer-quirks.md</c>.
///
/// Nothing here knows about receipts, money or layout.
/// </summary>
public static class EscPos
{
    /// <summary>Escape, <c>0x1B</c>.</summary>
    public const byte Esc = 0x1B;

    /// <summary>Group separator, <c>0x1D</c>. Introduces the <c>GS</c> commands.</summary>
    public const byte Gs = 0x1D;

    /// <summary>Line feed, <c>0x0A</c>. Prints the buffered line and advances.</summary>
    public const byte Lf = 0x0A;

    /// <summary>Code page 437 (US/European), <c>ESC t 0</c>. The safe default for a Latin bill.</summary>
    public const byte CodePage437 = 0;

    /// <summary>Code page 1252 (Windows Latin 1), <c>ESC t 16</c>.</summary>
    public const byte CodePage1252 = 16;

    /// <summary>
    /// <c>ESC @</c> - initialise. Clears the buffer and resets alignment, emphasis and
    /// character size. Every document starts with this so a previous document's state cannot
    /// leak into it.
    /// </summary>
    public static ReadOnlySpan<byte> Initialise => [Esc, (byte)'@'];

    /// <summary><c>ESC E 1</c> - emphasis on.</summary>
    public static ReadOnlySpan<byte> BoldOn => [Esc, (byte)'E', 1];

    /// <summary><c>ESC E 0</c> - emphasis off.</summary>
    public static ReadOnlySpan<byte> BoldOff => [Esc, (byte)'E', 0];

    /// <summary><c>ESC a n</c> - horizontal alignment.</summary>
    public static byte[] Align(TextAlign align) => [Esc, (byte)'a', (byte)align];

    /// <summary><c>ESC t n</c> - select the character code table.</summary>
    public static byte[] SelectCodePage(byte codePage) => [Esc, (byte)'t', codePage];

    /// <summary>
    /// <c>GS ! n</c> - character size. The high nibble is the width multiplier and the low
    /// nibble the height multiplier, each zero-based; <c>0x11</c> is double both ways, which
    /// is what the bill total prints in (SRS PRT-03).
    /// </summary>
    public static byte[] CharacterSize(bool doubleWidth, bool doubleHeight)
    {
        var n = (byte)((doubleWidth ? 0x10 : 0x00) | (doubleHeight ? 0x01 : 0x00));

        return [Gs, (byte)'!', n];
    }

    /// <summary><c>ESC d n</c> - feed <paramref name="lines"/> lines.</summary>
    public static byte[] FeedLines(int lines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lines, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lines, 255);

        return [Esc, (byte)'d', (byte)lines];
    }

    /// <summary>
    /// <c>GS V</c> - cut the paper (SRS PRT-05). The two-argument forms feed
    /// <paramref name="feedLines"/> first; the one-argument forms ignore it and expect the
    /// caller to have fed already.
    /// </summary>
    public static byte[] Cut(CutMode mode, int feedLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(feedLines, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(feedLines, 255);

        return mode switch
        {
            CutMode.Partial or CutMode.Full => [Gs, (byte)'V', (byte)mode],
            CutMode.FeedAndPartial or CutMode.FeedAndFull =>
                [Gs, (byte)'V', (byte)mode, (byte)feedLines],
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown cut mode."),
        };
    }

    /// <summary>
    /// <c>ESC p m t1 t2</c> - pulse the drawer kick-out port (SRS FR-7.7). The defaults are
    /// the conventional 25 ms on, 250 ms off (each unit is 2 ms).
    /// </summary>
    public static byte[] DrawerKick(DrawerPin pin, byte onTime = 25, byte offTime = 250) =>
        [Esc, (byte)'p', (byte)pin, onTime, offTime];

    /// <summary><c>GS h n</c> - barcode height in dots.</summary>
    public static byte[] BarcodeHeight(byte dots) => [Gs, (byte)'h', dots];

    /// <summary><c>GS w n</c> - barcode module width, 2 to 6 dots.</summary>
    public static byte[] BarcodeModuleWidth(byte width) => [Gs, (byte)'w', width];

    /// <summary><c>GS H n</c> - where to print the human-readable digits.</summary>
    public static byte[] BarcodeHriPosition(HriPosition position) =>
        [Gs, (byte)'H', (byte)position];

    /// <summary><c>GS f n</c> - which font the human-readable digits use. 0 is font A.</summary>
    public static byte[] BarcodeHriFont(byte font) => [Gs, (byte)'f', font];

    /// <summary>
    /// <c>GS k m n d1...dn</c> - print a 1D barcode, function B (explicit length).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The encoded data is empty or longer than 255 bytes, which the command cannot express.
    /// </exception>
    public static byte[] Barcode(BarcodeSymbology symbology, ReadOnlySpan<byte> data)
    {
        if (data.Length is 0 or > 255)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"GS k carries 1 to 255 data bytes, not {data.Length}."),
                nameof(data));
        }

        var command = new byte[4 + data.Length];
        command[0] = Gs;
        command[1] = (byte)'k';
        command[2] = (byte)symbology;
        command[3] = (byte)data.Length;
        data.CopyTo(command.AsSpan(4));

        return command;
    }

    /// <summary><c>GS ( k</c> - select QR model 2.</summary>
    public static ReadOnlySpan<byte> QrCodeSelectModel =>
        [Gs, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0];

    /// <summary><c>GS ( k</c> - QR module size in dots.</summary>
    public static byte[] QrCodeModuleSize(byte dots) =>
        [Gs, (byte)'(', (byte)'k', 3, 0, 49, 67, dots];

    /// <summary>
    /// <c>GS ( k</c> - QR error correction level: 48 = L, 49 = M, 50 = Q, 51 = H.
    /// </summary>
    public static byte[] QrCodeErrorCorrection(byte level) =>
        [Gs, (byte)'(', (byte)'k', 3, 0, 49, 69, level];

    /// <summary>
    /// <c>GS ( k</c> - store the data in the symbol buffer, ready to print.
    /// </summary>
    /// <exception cref="ArgumentException">The data does not fit the command's length field.</exception>
    public static byte[] QrCodeStore(ReadOnlySpan<byte> data)
    {
        var length = data.Length + 3;

        if (data.Length is 0 || length > 0xFFFF)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A QR symbol carries 1 to 65532 data bytes, not {data.Length}."),
                nameof(data));
        }

        var command = new byte[8 + data.Length];
        command[0] = Gs;
        command[1] = (byte)'(';
        command[2] = (byte)'k';
        command[3] = (byte)(length & 0xFF);
        command[4] = (byte)((length >> 8) & 0xFF);
        command[5] = 49;
        command[6] = 80;
        command[7] = 48;
        data.CopyTo(command.AsSpan(8));

        return command;
    }

    /// <summary><c>GS ( k</c> - print what is in the symbol buffer.</summary>
    public static ReadOnlySpan<byte> QrCodePrint =>
        [Gs, (byte)'(', (byte)'k', 3, 0, 49, 81, 48];

    /// <summary>
    /// <c>GS v 0 m xL xH yL yH d1...dk</c> - print a raster bitmap at normal scale.
    /// </summary>
    /// <exception cref="ArgumentException">The image is wider or taller than the command can express.</exception>
    public static byte[] RasterBitmap(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var bytesPerRow = image.BytesPerRow;

        if (bytesPerRow > 0xFFFF || image.HeightDots > 0xFFFF)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"GS v 0 cannot express a {image.WidthDots} x {image.HeightDots} raster."),
                nameof(image));
        }

        var bits = image.Bits.Span;
        var command = new byte[8 + bits.Length];
        command[0] = Gs;
        command[1] = (byte)'v';
        command[2] = (byte)'0';
        command[3] = 0;
        command[4] = (byte)(bytesPerRow & 0xFF);
        command[5] = (byte)((bytesPerRow >> 8) & 0xFF);
        command[6] = (byte)(image.HeightDots & 0xFF);
        command[7] = (byte)((image.HeightDots >> 8) & 0xFF);
        bits.CopyTo(command.AsSpan(8));

        return command;
    }
}
