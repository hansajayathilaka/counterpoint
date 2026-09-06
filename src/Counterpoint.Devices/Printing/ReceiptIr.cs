using System;
using System.Collections.Generic;
using System.Linq;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// The receipt intermediate representation: what to print, never how to print it.
///
/// A template (P1-T11) produces a <see cref="ReceiptDocument"/> of these nodes; the
/// <see cref="EscPosRenderer"/> turns them into bytes. Nothing above the renderer knows an
/// escape code, and nothing below it knows what a bill is - which is what lets the layout stay
/// owner-editable (SRS FR-7.3, FR-10.8) and lets a printer quirk be fixed in one place.
///
/// <para>
/// The hierarchy is closed to callers and open to the renderer: adding a node - the
/// <c>RasterText</c> that a second language would need (Q-C), a logo - is a new nested record
/// and a new arm in the renderer's switch. No existing caller changes.
/// </para>
///
/// <para>
/// This file holds both <see cref="ReceiptNode"/> and <see cref="ReceiptDocument"/> on
/// purpose: they are one concept, and the nodes are nested so they read as
/// <c>ReceiptNode.TextLine</c> at every call site.
/// </para>
/// </summary>
public abstract record ReceiptNode
{
    private ReceiptNode()
    {
    }

    /// <summary>
    /// One line of text. Longer than the paper is wide, it wraps onto continuation lines
    /// rather than being cut off (SRS PRT-02).
    /// </summary>
    /// <param name="Text">The text. May be empty, which prints a blank line.</param>
    /// <param name="Align">Where on the line it sits.</param>
    /// <param name="Bold">Emphasis.</param>
    /// <param name="DoubleHeight">Twice as tall.</param>
    /// <param name="DoubleWidth">
    /// Twice as wide, which halves how many characters fit. The bill total prints double both
    /// ways (SRS PRT-03).
    /// </param>
    public sealed record TextLine(
        string Text,
        TextAlign Align = TextAlign.Left,
        bool Bold = false,
        bool DoubleHeight = false,
        bool DoubleWidth = false) : ReceiptNode;

    /// <summary>
    /// A description on the left and an amount on the right, the amount right-aligned in the
    /// money column so decimal points line up down the bill. The description wraps; the amount
    /// never does, and lands on the last line of the description.
    /// </summary>
    /// <param name="Left">The description. May be empty.</param>
    /// <param name="Right">The amount, already formatted as text.</param>
    /// <param name="Bold">Emphasis.</param>
    /// <param name="DoubleHeight">Twice as tall.</param>
    /// <param name="DoubleWidth">Twice as wide, which halves the usable width.</param>
    public sealed record Columns(
        string Left,
        string Right,
        bool Bold = false,
        bool DoubleHeight = false,
        bool DoubleWidth = false) : ReceiptNode;

    /// <summary>A horizontal rule across the full width of the paper.</summary>
    public sealed record Divider : ReceiptNode;

    /// <summary>
    /// A 1D barcode - the bill number, so a return can be started by scanning the customer's
    /// receipt (SRS PRT-04, FR-7.4). Whether it prints natively or as a raster is a printer
    /// capability, not a property of the receipt.
    /// </summary>
    /// <param name="Data">The value to encode.</param>
    /// <param name="Symbology">How to encode it.</param>
    /// <param name="Align">Where on the line it sits.</param>
    public sealed record Barcode(
        string Data,
        BarcodeSymbology Symbology = BarcodeSymbology.Code128,
        TextAlign Align = TextAlign.Centre) : ReceiptNode;

    /// <summary>
    /// A barcode that has already been rendered to a bitmap, printed with <c>GS v 0</c>.
    ///
    /// <para>
    /// This node exists from day one even though <see cref="BarcodeMode.Native"/> is the
    /// default, because the fallback it enables is the one printer quirk we can be confident
    /// of meeting: a unit whose <c>GS k</c> is missing or wrong. <c>HW-T01</c> switches to it
    /// with a capability flag and an <see cref="IBarcodeRasteriser"/>, and no template or
    /// renderer caller changes.
    /// </para>
    /// </summary>
    /// <param name="Image">The rendered bars.</param>
    /// <param name="Data">The value the bars encode, kept for diagnostics and reprints.</param>
    /// <param name="Symbology">The symbology the bars were rendered in.</param>
    /// <param name="Align">Where on the line it sits.</param>
    public sealed record RasterBarcode(
        RasterImage Image,
        string Data,
        BarcodeSymbology Symbology = BarcodeSymbology.Code128,
        TextAlign Align = TextAlign.Centre) : ReceiptNode;

    /// <summary>A QR code (SRS PRT-04) - the alternative to a 1D bill-number barcode.</summary>
    /// <param name="Data">The value to encode.</param>
    /// <param name="Align">Where on the line it sits.</param>
    public sealed record QrCode(string Data, TextAlign Align = TextAlign.Centre) : ReceiptNode;

    /// <summary>Blank vertical space.</summary>
    /// <param name="Lines">How many lines to feed. Zero prints nothing.</param>
    public sealed record Feed(int Lines = 1) : ReceiptNode;

    /// <summary>
    /// Cut the paper (SRS PRT-05). Silently skipped on a printer with no cutter.
    /// </summary>
    public sealed record Cut : ReceiptNode;

    /// <summary>
    /// Open the cash drawer (SRS FR-7.7). Only a cash tender or an authorised "no sale" puts
    /// this on a document.
    /// </summary>
    public sealed record Kick : ReceiptNode;
}

/// <summary>
/// A whole document, in order: the nodes a renderer walks to produce one print job.
/// </summary>
public sealed record ReceiptDocument
{
    /// <summary>Builds a document from an ordered sequence of nodes.</summary>
    /// <exception cref="ArgumentException">A node in the sequence is null.</exception>
    public ReceiptDocument(IEnumerable<ReceiptNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        Nodes = nodes.ToArray();

        if (Nodes.Any(node => node is null))
        {
            throw new ArgumentException("A receipt document cannot contain a null node.", nameof(nodes));
        }
    }

    /// <summary>The nodes, in print order.</summary>
    public IReadOnlyList<ReceiptNode> Nodes { get; }

    /// <summary>Builds a document from nodes written out inline.</summary>
    public static ReceiptDocument Of(params ReceiptNode[] nodes) => new(nodes);
}
