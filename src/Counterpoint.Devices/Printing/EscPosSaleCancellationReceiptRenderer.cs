using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a cancelled bill into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/> (SRS FR-3.34).
/// </summary>
/// <remarks>
/// A fixed layout, the same deliberate choice <see cref="EscPosSaleReceiptRenderer"/> made for
/// the sale receipt itself (P0-T05): the owner-editable Scriban template is P1-T11's, for both
/// documents at once. Pure - no device, no file, no clock - so it is safe to call inside the
/// cancellation's transaction (CLAUDE.md invariant 7).
/// </remarks>
public sealed class EscPosSaleCancellationReceiptRenderer : ISaleCancellationReceiptRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - the
    /// total arriving here was already rounded when the original bill was completed (CLAUDE.md
    /// invariant 2).
    /// </param>
    public EscPosSaleCancellationReceiptRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(SaleCancellationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("BILL CANCELLED", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Bill No   : " + receipt.BillNo),
            new ReceiptNode.TextLine(
                "Sold      : " + receipt.SoldAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine(
                "Cancelled : " + receipt.CancelledAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine("By        : " + receipt.CancelledBy),
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns(
                "Original total",
                FormatAmount(receipt.Total),
                Bold: true,
                DoubleHeight: true,
                DoubleWidth: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Reason: " + receipt.Reason),
            new ReceiptNode.Feed(1),
            new ReceiptNode.Barcode(receipt.BillNo),
            new ReceiptNode.Cut(),
        };

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <summary>
    /// Money as it reads on a receipt. Formatting only - it does not round, because the amount
    /// was rounded at the two points that are allowed to, when the original bill was completed.
    /// </summary>
    private string FormatAmount(Money amount)
    {
        var format = _rounding.DecimalPlaces > 0
            ? "0." + new string('0', _rounding.DecimalPlaces)
            : "0";

        return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
    }
}
