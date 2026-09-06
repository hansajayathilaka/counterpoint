using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a <see cref="ReceiptDocument"/> into the byte stream a thermal printer eats
/// (SRS FR-7.1, §10).
///
/// <para>
/// Layout is arithmetic on characters, not guesswork: 80 mm paper is
/// <see cref="ReceiptLayout.CharactersPerLine"/> characters in font A - 48 by default, 32 on
/// 58 mm (SRS PRT-01). Descriptions wrap onto continuation lines rather than being cut off
/// (SRS PRT-02); amounts are right-aligned in a fixed money column so the decimal points line
/// up; double-width text halves the usable width and the wrapping follows it (SRS PRT-03).
/// </para>
///
/// <para>
/// Every command is gated on a <see cref="PrinterCapabilities"/> flag. A printer with no
/// cutter, no code-page command or an unusable native barcode still prints a correct bill -
/// which is the point, because the renderer must not be edited when <c>HW-T01</c> meets the
/// real unit.
/// </para>
///
/// <para>The renderer is stateless between calls and safe to share.</para>
/// </summary>
public sealed class EscPosRenderer
{
    private readonly PrinterCapabilities _capabilities;
    private readonly IBarcodeRasteriser? _barcodeRasteriser;

    /// <summary>A renderer for a standard 80 mm ESC/POS printer.</summary>
    public EscPosRenderer()
        : this(PrinterCapabilities.Default)
    {
    }

    /// <summary>
    /// A renderer for a specific printer.
    /// </summary>
    /// <param name="capabilities">What that printer can be trusted to do.</param>
    /// <param name="barcodeRasteriser">
    /// Required only when <see cref="PrinterCapabilities.BarcodeMode"/> is
    /// <see cref="BarcodeMode.Raster"/>. There is no implementation in the software track;
    /// <c>HW-T01</c> supplies one if the shop's printer needs it.
    /// </param>
    /// <exception cref="ArgumentException">The capabilities describe an unusable printer.</exception>
    public EscPosRenderer(PrinterCapabilities capabilities, IBarcodeRasteriser? barcodeRasteriser = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        capabilities.Layout.Validate();

        if (capabilities.BarcodeMode == BarcodeMode.Raster && barcodeRasteriser is null)
        {
            throw new ArgumentException(
                "BarcodeMode.Raster needs an IBarcodeRasteriser. Wire one in the composition "
                + "root, or leave the printer on BarcodeMode.Native.",
                nameof(barcodeRasteriser));
        }

        _capabilities = capabilities;
        _barcodeRasteriser = barcodeRasteriser;
    }

    /// <summary>The printer this renderer is rendering for.</summary>
    public PrinterCapabilities Capabilities => _capabilities;

    /// <summary>
    /// Renders a document. The result is deterministic: the same document and capabilities
    /// always produce the same bytes, which is what makes the snapshot test meaningful.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public byte[] Render(ReceiptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sink = new List<byte>(2048);
        var state = new RenderState();

        if (_capabilities.SupportsInitialise)
        {
            sink.AddRange(EscPos.Initialise);

            // ESC @ leaves the printer left-aligned, at normal size, with emphasis off.
            state.Align = TextAlign.Left;
        }

        if (_capabilities.SupportsCodePage)
        {
            sink.AddRange(EscPos.SelectCodePage(_capabilities.CodePage));
        }

        foreach (var node in document.Nodes)
        {
            Write(sink, state, node);
        }

        return [.. sink];
    }

    private void Write(List<byte> sink, RenderState state, ReceiptNode node)
    {
        switch (node)
        {
            case ReceiptNode.TextLine text:
                WriteText(sink, state, text.Text, text.Align, text.Bold, text.DoubleHeight, text.DoubleWidth);
                break;

            case ReceiptNode.Columns columns:
                WriteColumns(sink, state, columns);
                break;

            case ReceiptNode.Divider:
                WriteText(
                    sink,
                    state,
                    new string(_capabilities.Layout.DividerCharacter, _capabilities.Layout.CharactersPerLine),
                    TextAlign.Left,
                    bold: false,
                    doubleHeight: false,
                    doubleWidth: false);
                break;

            case ReceiptNode.Barcode barcode:
                WriteBarcode(sink, state, barcode);
                break;

            case ReceiptNode.RasterBarcode raster:
                WriteRaster(sink, state, raster.Image, raster.Align);
                break;

            case ReceiptNode.QrCode qr:
                WriteQrCode(sink, state, qr);
                break;

            case ReceiptNode.Feed feed:
                if (feed.Lines > 0)
                {
                    sink.AddRange(EscPos.FeedLines(feed.Lines));
                }

                break;

            case ReceiptNode.Cut:
                WriteCut(sink);
                break;

            case ReceiptNode.Kick:
                if (_capabilities.SupportsDrawerKick)
                {
                    sink.AddRange(EscPos.DrawerKick(_capabilities.DrawerPin));
                }

                break;

            default:
                throw new NotSupportedException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"No renderer arm for receipt node {node.GetType().Name}."));
        }
    }

    private void WriteText(
        List<byte> sink,
        RenderState state,
        string text,
        TextAlign align,
        bool bold,
        bool doubleHeight,
        bool doubleWidth)
    {
        var width = EffectiveWidth(doubleWidth);

        foreach (var line in Wrap(text, width))
        {
            // Where the printer cannot align for us, pad to the same effect.
            var content = _capabilities.SupportsAlign ? line : PadTo(line, align, width);

            SetAlign(sink, state, align);
            StyleOn(sink, bold, doubleHeight, doubleWidth);
            sink.AddRange(_capabilities.TextEncoding.GetBytes(content));
            StyleOff(sink, bold, doubleHeight, doubleWidth);
            sink.Add(EscPos.Lf);
        }
    }

    private void WriteColumns(List<byte> sink, RenderState state, ReceiptNode.Columns columns)
    {
        var width = EffectiveWidth(columns.DoubleWidth);
        var moneyWidth = Math.Min(_capabilities.Layout.MoneyColumnWidth, width);

        // The amount is never wrapped: it takes the space it needs, up to the whole line.
        var amountWidth = Math.Clamp(columns.Right.Length, moneyWidth, width);
        var amount = Truncate(columns.Right, amountWidth).PadLeft(amountWidth, ' ');
        var descriptionWidth = width - amountWidth;

        var lines = new List<string>();

        if (descriptionWidth <= 0)
        {
            lines.Add(amount);
        }
        else
        {
            var wrapped = Wrap(columns.Left, descriptionWidth);

            for (var i = 0; i < wrapped.Count - 1; i++)
            {
                lines.Add(wrapped[i]);
            }

            lines.Add(wrapped[^1].PadRight(descriptionWidth, ' ') + amount);
        }

        foreach (var line in lines)
        {
            SetAlign(sink, state, TextAlign.Left);
            StyleOn(sink, columns.Bold, columns.DoubleHeight, columns.DoubleWidth);
            sink.AddRange(_capabilities.TextEncoding.GetBytes(line));
            StyleOff(sink, columns.Bold, columns.DoubleHeight, columns.DoubleWidth);
            sink.Add(EscPos.Lf);
        }
    }

    private void WriteBarcode(List<byte> sink, RenderState state, ReceiptNode.Barcode barcode)
    {
        if (_capabilities.BarcodeMode == BarcodeMode.Raster)
        {
            var image = Rasteriser.RasteriseBarcode(
                barcode.Data,
                barcode.Symbology,
                _capabilities.Layout.PrintWidthDots,
                _capabilities.BarcodeHeightDots);

            WriteRaster(sink, state, image, barcode.Align);
            return;
        }

        var data = barcode.Data;

        if (barcode.Symbology == BarcodeSymbology.Code128
            && _capabilities.PrefixCode128CodeSet
            && !data.StartsWith('{'))
        {
            // Code set B: the printable ASCII set a bill number lives in.
            data = "{B" + data;
        }

        SetAlign(sink, state, barcode.Align);
        sink.AddRange(EscPos.BarcodeHeight(_capabilities.BarcodeHeightDots));
        sink.AddRange(EscPos.BarcodeModuleWidth(_capabilities.BarcodeModuleWidth));
        sink.AddRange(EscPos.BarcodeHriPosition(_capabilities.BarcodeHriPosition));
        sink.AddRange(EscPos.BarcodeHriFont(_capabilities.BarcodeHriFont));
        sink.AddRange(EscPos.Barcode(barcode.Symbology, _capabilities.TextEncoding.GetBytes(data)));
    }

    private void WriteQrCode(List<byte> sink, RenderState state, ReceiptNode.QrCode qr)
    {
        if (_capabilities.BarcodeMode == BarcodeMode.Raster)
        {
            var image = Rasteriser.RasteriseQrCode(qr.Data, _capabilities.Layout.PrintWidthDots);

            WriteRaster(sink, state, image, qr.Align);
            return;
        }

        if (!_capabilities.SupportsQrCode)
        {
            // Degrade: a printer that cannot draw a QR code still prints the rest of the bill.
            return;
        }

        SetAlign(sink, state, qr.Align);
        sink.AddRange(EscPos.QrCodeSelectModel);
        sink.AddRange(EscPos.QrCodeModuleSize(_capabilities.QrCodeModuleSize));
        sink.AddRange(EscPos.QrCodeErrorCorrection(_capabilities.QrCodeErrorCorrection));
        sink.AddRange(EscPos.QrCodeStore(_capabilities.TextEncoding.GetBytes(qr.Data)));
        sink.AddRange(EscPos.QrCodePrint);
    }

    private void WriteRaster(List<byte> sink, RenderState state, RasterImage image, TextAlign align)
    {
        if (!_capabilities.SupportsRaster)
        {
            return;
        }

        SetAlign(sink, state, align);
        sink.AddRange(EscPos.RasterBitmap(image));
    }

    private void WriteCut(List<byte> sink)
    {
        if (!_capabilities.SupportsCut)
        {
            // Degrade: no cutter means the cashier tears the paper off. Not a failed print.
            return;
        }

        var feed = _capabilities.FeedLinesBeforeCut;
        var cutFeedsItself = _capabilities.CutMode is CutMode.FeedAndPartial or CutMode.FeedAndFull;

        if (feed > 0 && !cutFeedsItself)
        {
            sink.AddRange(EscPos.FeedLines(feed));
        }

        sink.AddRange(EscPos.Cut(_capabilities.CutMode, feed));
    }

    private void SetAlign(List<byte> sink, RenderState state, TextAlign align)
    {
        if (!_capabilities.SupportsAlign || state.Align == align)
        {
            return;
        }

        sink.AddRange(EscPos.Align(align));
        state.Align = align;
    }

    private void StyleOn(List<byte> sink, bool bold, bool doubleHeight, bool doubleWidth)
    {
        if ((doubleHeight || doubleWidth) && _capabilities.SupportsDoubleSize)
        {
            sink.AddRange(EscPos.CharacterSize(doubleWidth, doubleHeight));
        }

        if (bold && _capabilities.SupportsBold)
        {
            sink.AddRange(EscPos.BoldOn);
        }
    }

    private void StyleOff(List<byte> sink, bool bold, bool doubleHeight, bool doubleWidth)
    {
        if (bold && _capabilities.SupportsBold)
        {
            sink.AddRange(EscPos.BoldOff);
        }

        if ((doubleHeight || doubleWidth) && _capabilities.SupportsDoubleSize)
        {
            sink.AddRange(EscPos.CharacterSize(doubleWidth: false, doubleHeight: false));
        }
    }

    private IBarcodeRasteriser Rasteriser =>
        _barcodeRasteriser
        ?? throw new InvalidOperationException(
            "BarcodeMode.Raster was selected without an IBarcodeRasteriser.");

    /// <summary>Characters that fit on one line at the given size.</summary>
    private int EffectiveWidth(bool doubleWidth) =>
        doubleWidth && _capabilities.SupportsDoubleSize
            ? _capabilities.Layout.CharactersPerLine / 2
            : _capabilities.Layout.CharactersPerLine;

    /// <summary>
    /// Breaks <paramref name="text"/> into printable lines.
    ///
    /// Text that already fits is returned untouched, spacing and all - templates pad their own
    /// sub-columns and that padding must survive. Only text that does not fit is re-flowed,
    /// on word boundaries (SRS PRT-02). A single word longer than the paper - a part number
    /// with no spaces in it - is split across lines rather than truncated, because losing the
    /// tail of a part number is worse than an ugly break.
    /// </summary>
    private static List<string> Wrap(string text, int width)
    {
        if (width <= 0)
        {
            return [];
        }

        if (text.Length <= width)
        {
            return [text];
        }

        var lines = new List<string>();
        var current = new StringBuilder(width);

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var remaining = word;

            if (remaining.Length > width)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                while (remaining.Length > width)
                {
                    lines.Add(remaining[..width]);
                    remaining = remaining[width..];
                }

                if (remaining.Length > 0)
                {
                    current.Append(remaining);
                }

                continue;
            }

            if (current.Length == 0)
            {
                current.Append(remaining);
            }
            else if (current.Length + 1 + remaining.Length <= width)
            {
                current.Append(' ').Append(remaining);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(remaining);
            }
        }

        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>
    /// Cuts text that cannot fit. Only the money column can reach this: a description wraps
    /// instead. Losing the end of an amount means the caller formatted something absurd.
    /// </summary>
    private static string Truncate(string text, int width) =>
        text.Length <= width ? text : text[..width];

    /// <summary>Alignment by padding, for a printer that has no <c>ESC a</c>.</summary>
    private static string PadTo(string text, TextAlign align, int width)
    {
        if (text.Length >= width)
        {
            return text;
        }

        return align switch
        {
            TextAlign.Centre => new string(' ', (width - text.Length) / 2) + text,
            TextAlign.Right => text.PadLeft(width, ' '),
            _ => text,
        };
    }

    /// <summary>What the printer is currently set to, so the stream carries no redundant commands.</summary>
    private sealed class RenderState
    {
        /// <summary>Null until the renderer has set alignment at least once.</summary>
        public TextAlign? Align { get; set; }
    }
}
