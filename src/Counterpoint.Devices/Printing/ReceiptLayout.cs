using System;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// The paper's dimensions in characters and dots (SRS PRT-01: 80 mm is the default and 58 mm
/// must be reachable by configuration).
///
/// Nothing in the renderer hard-codes 48. Everything measures against
/// <see cref="CharactersPerLine"/>, so switching the shop to 58 mm paper is a settings change,
/// not a code change.
/// </summary>
public sealed record ReceiptLayout
{
    /// <summary>
    /// Characters on one line in font A. 48 on 80 mm paper, 32 on 58 mm.
    /// </summary>
    public required int CharactersPerLine { get; init; }

    /// <summary>
    /// Width of the money column at the right edge of a <see cref="ReceiptNode.Columns"/> row.
    /// Amounts are right-aligned inside it so the decimal points line up down the bill.
    /// </summary>
    public required int MoneyColumnWidth { get; init; }

    /// <summary>Printable width in dots, for rasters. 576 on 80 mm at 203 dpi, 384 on 58 mm.</summary>
    public required int PrintWidthDots { get; init; }

    /// <summary>
    /// The character a <see cref="ReceiptNode.Divider"/> repeats. ASCII by default: the box
    /// drawing characters in the SRS specimen are not in code page 437 and would print as
    /// noise on most units.
    /// </summary>
    public char DividerCharacter { get; init; } = '-';

    /// <summary>80 mm paper: 48 characters, 576 dots. The default.</summary>
    public static ReceiptLayout EightyMillimetre { get; } = new()
    {
        CharactersPerLine = 48,
        MoneyColumnWidth = 11,
        PrintWidthDots = 576,
    };

    /// <summary>58 mm paper: 32 characters, 384 dots (SRS PRT-01).</summary>
    public static ReceiptLayout FiftyEightMillimetre { get; } = new()
    {
        CharactersPerLine = 32,
        MoneyColumnWidth = 10,
        PrintWidthDots = 384,
    };

    /// <summary>
    /// Throws if the layout cannot hold a money column and at least one character beside it.
    /// </summary>
    /// <exception cref="ArgumentException">The layout is unusable.</exception>
    public void Validate()
    {
        if (CharactersPerLine < 16)
        {
            throw new ArgumentException(
                "A receipt line needs at least 16 characters. Check the paper width setting.");
        }

        if (MoneyColumnWidth < 4 || MoneyColumnWidth >= CharactersPerLine)
        {
            throw new ArgumentException(
                "The money column must be at least 4 characters and must leave room for a "
                + "description beside it. Check the paper width setting.");
        }
    }
}
