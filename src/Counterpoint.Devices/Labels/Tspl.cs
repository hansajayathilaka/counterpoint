using System.Globalization;
using System.Text;

namespace Counterpoint.Devices.Labels;

/// <summary>
/// The TSPL (Tag Stock Programming Language) command set, as bytes and nothing else (SRS FR-2.10,
/// FR-2.12) - the label-printer analogue of <c>Counterpoint.Devices.Printing.EscPos</c>.
///
/// <para>
/// TSPL is line-oriented ASCII: each command is one line, terminated <c>CR LF</c>, and every
/// clone (TSC, Zebra's GK/GC series, most no-name thermal transfer units) accepts this subset.
/// Nothing here knows about a product, a batch or a layout - that is
/// <see cref="TsplLabelRenderer"/>'s job. When the shop's actual printer turns out to want a
/// quirk, the fix is here, in one command, exactly as <c>EscPos</c> is designed to take one
/// (<c>HW-T03</c> records what it found).
/// </para>
/// </summary>
public static class Tspl
{
    private static readonly Encoding CommandEncoding = Encoding.ASCII;

    /// <summary><c>SIZE w mm,h mm</c> - the label's printable area.</summary>
    public static byte[] Size(int widthMm, int heightMm) =>
        Command(string.Create(
            CultureInfo.InvariantCulture,
            $"SIZE {widthMm} mm,{heightMm} mm"));

    /// <summary><c>GAP g mm,0 mm</c> - the black mark or gap between labels on the roll.</summary>
    public static byte[] Gap(int gapMm) =>
        Command(string.Create(CultureInfo.InvariantCulture, $"GAP {gapMm} mm,0 mm"));

    /// <summary><c>DIRECTION n</c> - print direction and mirroring; 1 is "away from the printer".</summary>
    public static byte[] Direction(int direction) =>
        Command(string.Create(CultureInfo.InvariantCulture, $"DIRECTION {direction}"));

    /// <summary><c>CLS</c> - clears the image buffer. Starts every label.</summary>
    public static byte[] Cls() => Command("CLS");

    /// <summary>
    /// <c>TEXT x,y,"font",rotation,x-mult,y-mult,"content"</c> - one line of text at a dot
    /// position.
    /// </summary>
    public static byte[] Text(
        int x,
        int y,
        string font,
        int rotation,
        int xMultiplier,
        int yMultiplier,
        string content) =>
        Command(string.Create(
            CultureInfo.InvariantCulture,
            $"TEXT {x},{y},\"{font}\",{rotation},{xMultiplier},{yMultiplier},\"{Escape(content)}\""));

    /// <summary>
    /// <c>BARCODE x,y,"type",height,human-readable,rotation,narrow,wide,"content"</c> - one 1D
    /// barcode symbol.
    /// </summary>
    public static byte[] Barcode(
        int x,
        int y,
        string symbology,
        int height,
        int humanReadable,
        int rotation,
        int narrow,
        int wide,
        string content) =>
        Command(string.Create(
            CultureInfo.InvariantCulture,
            $"BARCODE {x},{y},\"{symbology}\",{height},{humanReadable},{rotation},{narrow},{wide}"
            + $",\"{Escape(content)}\""));

    /// <summary>
    /// <c>PRINT sets,copies</c> - prints the buffered label. <paramref name="copies"/> is the
    /// quantity-per-label the caller asked for; the buffer is not cleared until the next
    /// <see cref="Cls"/>.
    /// </summary>
    public static byte[] Print(int sets, int copies) =>
        Command(string.Create(CultureInfo.InvariantCulture, $"PRINT {sets},{copies}"));

    private static byte[] Command(string line) => CommandEncoding.GetBytes(line + "\r\n");

    /// <summary>
    /// TSPL has no escape sequence for a double quote inside a quoted field, so one occurring in
    /// a product name or code is folded to an apostrophe rather than corrupting the command
    /// stream - the same defensive substitution a shop's own till roll would need for a symbol
    /// its printer cannot draw.
    /// </summary>
    private static string Escape(string content) => content.Replace('"', '\'');
}
