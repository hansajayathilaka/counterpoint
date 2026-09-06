using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Counterpoint.Device.Tests.Support;

/// <summary>
/// Renders an ESC/POS byte stream as something a person can review in a pull request.
///
/// A snapshot of raw bytes proves the stream did not change; it does not prove the stream is
/// right. This produces two views of the same bytes: a command listing, where a wrong
/// alignment or a missing cut is obvious at a glance, and a hex dump, which is exact. Both go
/// into the committed <c>.verified.txt</c>, so a diff shows both what changed and what it
/// means.
///
/// It is a test tool. The printer never sees any of this.
/// </summary>
internal static class EscPosDump
{
    /// <summary>Command listing followed by a hex dump.</summary>
    public static string Describe(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var text = new StringBuilder();

        text.AppendLine("=== commands ===");

        foreach (var line in Commands(bytes))
        {
            text.AppendLine(line);
        }

        text.AppendLine();
        text.AppendLine("=== bytes ===");

        foreach (var line in HexDump(bytes))
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    /// <summary>
    /// What lands on the paper: the printable text, split into lines on every line feed, with
    /// the commands stripped out. This is how a layout assertion reads the stream.
    /// </summary>
    public static List<string> PrintedLines(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var lines = new List<string>();
        var current = new StringBuilder();
        var offset = 0;

        while (offset < bytes.Length)
        {
            if (bytes[offset] == 0x0A)
            {
                lines.Add(current.ToString());
                current.Clear();
                offset++;
                continue;
            }

            var (length, description) = Decode(bytes, offset);

            if (description.StartsWith("TEXT", StringComparison.Ordinal))
            {
                current.Append(Encoding.ASCII.GetString(bytes, offset, length));
            }

            offset += length;
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static List<string> Commands(byte[] bytes)
    {
        var lines = new List<string>();
        var offset = 0;

        while (offset < bytes.Length)
        {
            var (length, description) = Decode(bytes, offset);

            lines.Add(Offset(offset) + "  " + description);
            offset += length;
        }

        return lines;
    }

    private static (int Length, string Description) Decode(byte[] bytes, int offset)
    {
        var b = bytes[offset];

        return b switch
        {
            0x1B => DecodeEsc(bytes, offset),
            0x1D => DecodeGs(bytes, offset),
            0x0A => (1, "LF"),
            _ => DecodeText(bytes, offset),
        };
    }

    private static (int Length, string Description) DecodeEsc(byte[] bytes, int offset)
    {
        var command = (char)At(bytes, offset + 1);

        return command switch
        {
            '@' => (2, "ESC @        initialise"),
            'a' => (3, "ESC a " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                + "      align " + AlignName(At(bytes, offset + 2))),
            'E' => (3, "ESC E " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                + "      bold " + (At(bytes, offset + 2) == 0 ? "off" : "on")),
            't' => (3, "ESC t " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                + "      select code page"),
            'd' => (3, "ESC d " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                + "      feed lines"),
            'p' => (5, string.Create(
                CultureInfo.InvariantCulture,
                $"ESC p {At(bytes, offset + 2)} {At(bytes, offset + 3)} {At(bytes, offset + 4)}  "
                + $"drawer kick (pin {(At(bytes, offset + 2) == 0 ? 2 : 5)})")),
            _ => (2, "ESC " + Hex(At(bytes, offset + 1)) + "     unrecognised"),
        };
    }

    private static (int Length, string Description) DecodeGs(byte[] bytes, int offset)
    {
        var command = (char)At(bytes, offset + 1);

        switch (command)
        {
            case '!':
                var size = At(bytes, offset + 2);
                return (3, "GS ! " + Hex(size) + "      character size ("
                    + ((size & 0x10) != 0 ? "double width" : "normal width") + ", "
                    + ((size & 0x01) != 0 ? "double height" : "normal height") + ")");

            case 'V':
                var mode = At(bytes, offset + 2);
                return mode is 65 or 66
                    ? (4, string.Create(
                        CultureInfo.InvariantCulture,
                        $"GS V {mode} {At(bytes, offset + 3)}   feed and cut"))
                    : (3, string.Create(
                        CultureInfo.InvariantCulture,
                        $"GS V {mode}       {(mode == 0 ? "full" : "partial")} cut"));

            case 'h':
                return (3, "GS h " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                    + "      barcode height (dots)");

            case 'w':
                return (3, "GS w " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                    + "       barcode module width");

            case 'H':
                return (3, "GS H " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                    + "       barcode HRI position");

            case 'f':
                return (3, "GS f " + At(bytes, offset + 2).ToString(CultureInfo.InvariantCulture)
                    + "       barcode HRI font");

            case 'k':
                var symbology = At(bytes, offset + 2);
                var dataLength = At(bytes, offset + 3);
                var data = Encoding.ASCII.GetString(bytes, offset + 4, dataLength);
                return (4 + dataLength, string.Create(
                    CultureInfo.InvariantCulture,
                    $"GS k {symbology}      barcode {SymbologyName(symbology)} \"{data}\""));

            case 'v':
                var bytesPerRow = At(bytes, offset + 4) | (At(bytes, offset + 5) << 8);
                var rows = At(bytes, offset + 6) | (At(bytes, offset + 7) << 8);
                return (8 + (bytesPerRow * rows), string.Create(
                    CultureInfo.InvariantCulture,
                    $"GS v 0       raster {bytesPerRow * 8} x {rows} dots "
                    + $"({bytesPerRow * rows} bytes)"));

            case '(':
                var payload = At(bytes, offset + 3) | (At(bytes, offset + 4) << 8);
                return (5 + payload, "GS ( k       " + QrFunctionName(bytes, offset));

            default:
                return (2, "GS " + Hex(At(bytes, offset + 1)) + "      unrecognised");
        }
    }

    private static (int Length, string Description) DecodeText(byte[] bytes, int offset)
    {
        var end = offset;

        while (end < bytes.Length && bytes[end] is >= 0x20 and < 0x7F)
        {
            end++;
        }

        if (end == offset)
        {
            return (1, "?? " + Hex(bytes[offset]) + "       unrecognised byte");
        }

        var text = Encoding.ASCII.GetString(bytes, offset, end - offset);

        return (end - offset, "TEXT         \"" + text + "\"");
    }

    private static string QrFunctionName(byte[] bytes, int offset) =>
        At(bytes, offset + 6) switch
        {
            65 => "QR select model",
            67 => "QR module size " + At(bytes, offset + 7).ToString(CultureInfo.InvariantCulture),
            69 => "QR error correction " + At(bytes, offset + 7).ToString(CultureInfo.InvariantCulture),
            80 => "QR store \""
                + Encoding.ASCII.GetString(
                    bytes,
                    offset + 8,
                    (At(bytes, offset + 3) | (At(bytes, offset + 4) << 8)) - 3)
                + "\"",
            81 => "QR print",
            _ => "QR unrecognised",
        };

    private static string SymbologyName(byte symbology) => symbology switch
    {
        65 => "UPC-A",
        67 => "EAN-13",
        68 => "EAN-8",
        69 => "Code 39",
        70 => "ITF",
        73 => "Code 128",
        _ => "unknown",
    };

    private static string AlignName(byte align) => align switch
    {
        0 => "left",
        1 => "centre",
        2 => "right",
        _ => "unknown",
    };

    private static List<string> HexDump(byte[] bytes)
    {
        var lines = new List<string>();

        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var hex = new StringBuilder();
            var ascii = new StringBuilder();

            for (var i = 0; i < 16; i++)
            {
                if (offset + i < bytes.Length)
                {
                    var b = bytes[offset + i];
                    hex.Append(Hex(b)).Append(' ');
                    ascii.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
                }
                else
                {
                    hex.Append("   ");
                }
            }

            lines.Add(Offset(offset) + "  " + hex.ToString().TrimEnd().PadRight(48) + "  |" + ascii + "|");
        }

        return lines;
    }

    private static byte At(byte[] bytes, int index) => index < bytes.Length ? bytes[index] : (byte)0;

    private static string Hex(byte value) => value.ToString("X2", CultureInfo.InvariantCulture);

    private static string Offset(int offset) => offset.ToString("X4", CultureInfo.InvariantCulture);
}
