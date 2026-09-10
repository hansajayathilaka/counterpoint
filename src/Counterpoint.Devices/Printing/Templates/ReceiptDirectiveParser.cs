using System;
using System.Collections.Generic;
using System.Globalization;

namespace Counterpoint.Devices.Printing.Templates;

/// <summary>
/// Turns the plain text a Scriban template renders into the receipt IR
/// (<see cref="ReceiptNode"/>) - the other half of what makes the layout owner-editable
/// (SRS FR-7.3, NFR-M1): the template decides <em>what</em> to print and in what order, this
/// decides nothing about layout at all, it only recognises a small line-directive language.
/// </summary>
/// <remarks>
/// <para>
/// One line of rendered text, one directive. A line whose first token (up to the first <c>|</c>)
/// matches a known keyword is parsed as that node; every other line - including a template that
/// uses no directives at all - prints exactly as written, left aligned. That fallback is
/// deliberate: an owner who only wants to reword the footer can type plain text and never learn
/// the directive language.
/// </para>
/// <para>
/// The directives, documented in full on
/// <c>Counterpoint.Application.Settings.ReceiptTemplateDefaults</c>:
/// <c>TEXT|align|bold|double|text</c>, <c>COLS|left|right|bold|double</c>, <c>DIV</c>,
/// <c>BARCODE|data</c>, <c>QR|data</c>, <c>FEED|n</c>, <c>CUT</c>, <c>KICK</c>.
/// </para>
/// </remarks>
public static class ReceiptDirectiveParser
{
    private const char FieldSeparator = '|';

    /// <summary>Parses the rendered template text into an ordered list of receipt nodes.</summary>
    /// <param name="renderedText">What <see cref="ScribanReceiptTemplateEngine"/> rendered.</param>
    public static IReadOnlyList<ReceiptNode> Parse(string renderedText)
    {
        ArgumentNullException.ThrowIfNull(renderedText);

        var nodes = new List<ReceiptNode>();

        foreach (var rawLine in renderedText.Split('\n'))
        {
            // A trailing \r from \r\n line endings - the rest of the line is content and is
            // left exactly as written (leading spaces matter to a COLS left side).
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;

            if (line.Length == 0)
            {
                nodes.Add(new ReceiptNode.TextLine(string.Empty));
                continue;
            }

            nodes.Add(ParseLine(line));
        }

        return nodes;
    }

    private static ReceiptNode ParseLine(string line)
    {
        var separatorIndex = line.IndexOf(FieldSeparator, StringComparison.Ordinal);
        var keyword = separatorIndex < 0 ? line : line[..separatorIndex];

        switch (keyword)
        {
            case "DIV":
                return new ReceiptNode.Divider();

            case "CUT":
                return new ReceiptNode.Cut();

            case "KICK":
                return new ReceiptNode.Kick();

            case "TEXT":
                return ParseText(line);

            case "COLS":
                return ParseColumns(line);

            case "BARCODE":
                return new ReceiptNode.Barcode(Field(line, 1));

            case "QR":
                return new ReceiptNode.QrCode(Field(line, 1));

            case "FEED":
                return new ReceiptNode.Feed(ParseInt(Field(line, 1), fallback: 1));

            default:
                // No directive matched: plain text, exactly as written (the "an owner who only
                // wants to reword a line needs no directive" fallback).
                return new ReceiptNode.TextLine(line);
        }
    }

    private static ReceiptNode.TextLine ParseText(string line)
    {
        var parts = line.Split(FieldSeparator, 5);

        if (parts.Length < 5)
        {
            // Malformed directive - fewer fields than TEXT needs. Printed as-is rather than
            // thrown away: a half-written template edit still produces a receipt (CLAUDE.md
            // invariant 7's spirit - nothing about rendering may make printing impossible).
            return new ReceiptNode.TextLine(line);
        }

        return new ReceiptNode.TextLine(
            parts[4],
            ParseAlign(parts[1]),
            Bold: ParseFlag(parts[2]),
            DoubleHeight: ParseFlag(parts[3]),
            DoubleWidth: ParseFlag(parts[3]));
    }

    private static ReceiptNode ParseColumns(string line)
    {
        var parts = line.Split(FieldSeparator, 5);

        if (parts.Length < 5)
        {
            return new ReceiptNode.TextLine(line);
        }

        return new ReceiptNode.Columns(
            parts[1],
            parts[2],
            Bold: ParseFlag(parts[3]),
            DoubleHeight: ParseFlag(parts[4]),
            DoubleWidth: ParseFlag(parts[4]));
    }

    private static string Field(string line, int index)
    {
        var parts = line.Split(FieldSeparator, index + 1);
        return parts.Length > index ? parts[index] : string.Empty;
    }

    private static TextAlign ParseAlign(string token) => token switch
    {
        "C" => TextAlign.Centre,
        "R" => TextAlign.Right,
        _ => TextAlign.Left,
    };

    private static bool ParseFlag(string token) => token == "1";

    private static int ParseInt(string token, int fallback) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
