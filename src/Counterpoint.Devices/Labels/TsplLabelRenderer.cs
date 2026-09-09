using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Services;

namespace Counterpoint.Devices.Labels;

/// <summary>
/// Turns a <see cref="LabelBatch"/> into TSPL bytes, through <see cref="Tspl"/> (SRS FR-2.10,
/// FR-2.12).
/// </summary>
/// <remarks>
/// <para>
/// A fixed layout - name, then barcode, then code, then unit and price, top to bottom - the same
/// arrangement the task names. The label is only ever one column wide (there is no case for
/// wrapping the way an 80 mm receipt wraps): a name too long for the label is left to the printer
/// to clip, which is what every TSPL printer does to a <c>TEXT</c> command on its own, and
/// exactly what a real label under <c>HW-T03</c> will show.
/// </para>
/// <para>
/// Positions are fixed dot offsets from the top-left corner, not scaled to
/// <see cref="LabelSettings.WidthMm"/>/<see cref="LabelSettings.HeightMm"/>: the byte stream has
/// to be deterministic for a fixed layout to snapshot-test at all, and precise on-label fitting
/// for the shop's actual stock is <c>HW-T03</c>'s job, not a software-only one (CLAUDE.md
/// "Hardware boundary").
/// </para>
/// <para>The renderer is stateless between calls and safe to share.</para>
/// </remarks>
public sealed class TsplLabelRenderer : ILabelRenderer
{
    /// <summary>203 dpi, the common resolution for a TSC/Zebra-class desktop label printer.</summary>
    private const int DotsPerMm = 8;

    private const int LeftMarginDots = 2 * DotsPerMm;
    private const int TopMarginDots = 2 * DotsPerMm;
    private const int NameLineHeightDots = 32;
    private const int BarcodeHeightDots = 64;
    private const int BarcodeHriAllowanceDots = 32;
    private const int DetailLineHeightDots = 24;

    private const string NameFont = "3";
    private const string DetailFont = "2";
    private const string BarcodeSymbology = "128";

    private readonly IRoundingPolicy _rounding;

    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places the price prints
    /// in - the amount arriving here is already rounded (CLAUDE.md invariant 2).
    /// </param>
    public TsplLabelRenderer(IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);

        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(LabelBatch batch, LabelSettings layout)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(layout);

        var sink = new List<byte>(256 * Math.Max(batch.Items.Count, 1));

        sink.AddRange(Tspl.Size(layout.WidthMm, layout.HeightMm));
        sink.AddRange(Tspl.Gap(layout.GapMm));
        sink.AddRange(Tspl.Direction(1));

        foreach (var item in batch.Items)
        {
            WriteLabel(sink, item, layout);
        }

        return [.. sink];
    }

    private void WriteLabel(List<byte> sink, LabelBatchItem item, LabelSettings layout)
    {
        sink.AddRange(Tspl.Cls());

        var y = TopMarginDots;

        if (layout.ShowProductName)
        {
            sink.AddRange(Tspl.Text(LeftMarginDots, y, NameFont, 0, 1, 1, item.ProductName));
            y += NameLineHeightDots;
        }

        if (layout.ShowBarcode)
        {
            sink.AddRange(Tspl.Barcode(
                LeftMarginDots,
                y,
                BarcodeSymbology,
                BarcodeHeightDots,
                humanReadable: 1,
                rotation: 0,
                narrow: 2,
                wide: 2,
                item.Barcode));
            y += BarcodeHeightDots + BarcodeHriAllowanceDots;
        }

        if (layout.ShowCode)
        {
            sink.AddRange(Tspl.Text(LeftMarginDots, y, DetailFont, 0, 1, 1, item.Code));
            y += DetailLineHeightDots;
        }

        if (layout.ShowUnit || layout.ShowPrice)
        {
            sink.AddRange(Tspl.Text(LeftMarginDots, y, DetailFont, 0, 1, 1, FormatUnitAndPrice(item, layout)));
        }

        sink.AddRange(Tspl.Print(1, item.QuantityPerLabel));
    }

    private string FormatUnitAndPrice(LabelBatchItem item, LabelSettings layout)
    {
        if (layout.ShowUnit && layout.ShowPrice)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{item.UomSymbol}  {FormatAmount(item)}");
        }

        return layout.ShowUnit ? item.UomSymbol : FormatAmount(item);
    }

    /// <summary>
    /// Money as the shelf reads it. Formatting only - it does not round, because the amount was
    /// rounded at the two points CLAUDE.md invariant 2 allows and this is neither of them.
    /// </summary>
    private string FormatAmount(LabelBatchItem item)
    {
        var format = _rounding.DecimalPlaces > 0
            ? "0." + new string('0', _rounding.DecimalPlaces)
            : "0";

        return item.UnitPrice.Amount.ToString(format, CultureInfo.InvariantCulture);
    }
}
