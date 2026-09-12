using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a completed return into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/> (SRS FR-5, FR-7.1, task P2-T02).
/// </summary>
/// <remarks>
/// A fixed layout, the same deliberate choice <see cref="EscPosSaleCancellationReceiptRenderer"/>
/// made for a cancellation slip (P1-T10): the owner-editable Scriban template engine (P1-T11) is
/// wired to the sales bill only, and extending it to a second document type is not part of this
/// task's brief. Pure - no device, no file, no clock - so it is safe to call inside the return
/// transaction (CLAUDE.md invariant 7).
/// </remarks>
public sealed class EscPosReturnReceiptRenderer : IReturnReceiptRenderer
{
    private const string CashRefundMethod = "CASH";

    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - every
    /// amount arriving here was already rounded when the return was priced (CLAUDE.md invariant 2).
    /// </param>
    public EscPosReturnReceiptRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(SaleReturnReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("RETURN", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Return No : " + receipt.ReturnNo),
            new ReceiptNode.TextLine("Bill No   : " + receipt.OriginalBillNo),
            new ReceiptNode.TextLine(
                "Returned  : " + receipt.ReturnedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine("By        : " + receipt.CashierName),
            new ReceiptNode.Divider(),
        };

        foreach (var line in receipt.Lines)
        {
            nodes.Add(new ReceiptNode.TextLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{line.Description} ({line.Disposition})")));
            nodes.Add(new ReceiptNode.Columns(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {line.Quantity.Value} {line.UomSymbol} @ {FormatAmount(line.UnitPrice)}"),
                FormatAmount(line.LineRefund)));
        }

        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.Columns("Subtotal", FormatAmount(receipt.Subtotal)));
        nodes.Add(new ReceiptNode.Columns("Tax", FormatAmount(receipt.Tax)));

        if (receipt.RestockingFee.IsPositive)
        {
            nodes.Add(new ReceiptNode.Columns("Restocking fee", "-" + FormatAmount(receipt.RestockingFee)));
        }

        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.Columns(
            "Total refund",
            FormatAmount(receipt.TotalRefund),
            Bold: true,
            DoubleHeight: true,
            DoubleWidth: true));
        nodes.Add(new ReceiptNode.TextLine("Refunded by " + receipt.RefundMethod));

        if (!string.IsNullOrWhiteSpace(receipt.PolicyText))
        {
            nodes.Add(new ReceiptNode.Feed(1));
            nodes.Add(new ReceiptNode.TextLine(receipt.PolicyText));
        }

        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.Barcode(receipt.ReturnNo));

        // A cash refund pays out of the drawer, the same as a cash tender does for a sale
        // (SRS FR-7.7, POS_Architecture_Design.md §8.3 step 7).
        if (string.Equals(receipt.RefundMethod, CashRefundMethod, StringComparison.Ordinal))
        {
            nodes.Add(new ReceiptNode.Kick());
        }

        nodes.Add(new ReceiptNode.Cut());

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <summary>
    /// Money as it reads on a receipt. Formatting only - it does not round, because every amount
    /// here was already rounded at one of the two points that are allowed to (CLAUDE.md
    /// invariant 2).
    /// </summary>
    private string FormatAmount(Money amount)
    {
        var format = _rounding.DecimalPlaces > 0
            ? "0." + new string('0', _rounding.DecimalPlaces)
            : "0";

        return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
    }
}
