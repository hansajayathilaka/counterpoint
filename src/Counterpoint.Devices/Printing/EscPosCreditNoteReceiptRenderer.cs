using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a freshly issued credit note into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/> (SRS FR-5 store credit, FR-7.1, task P2-T05).
/// </summary>
/// <remarks>
/// A fixed layout, the same deliberate choice <see cref="EscPosReturnReceiptRenderer"/> made for a
/// return receipt: the owner-editable Scriban template engine (P1-T11) is wired to the sales bill
/// only. Pure - no device, no file, no clock - so it is safe to call inside the return transaction
/// that issued the note (CLAUDE.md invariant 7).
/// </remarks>
public sealed class EscPosCreditNoteReceiptRenderer : ICreditNoteReceiptRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - the
    /// amount arriving here was already rounded when the return was priced (CLAUDE.md invariant 2).
    /// </param>
    public EscPosCreditNoteReceiptRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(CreditNoteReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("CREDIT NOTE", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Credit Note No : " + receipt.CreditNoteNumber),
            new ReceiptNode.TextLine("Return No      : " + receipt.ReturnNo),
            new ReceiptNode.TextLine(
                "Issued         : " + receipt.IssuedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine("By             : " + receipt.CashierName),
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns(
                "Amount",
                FormatAmount(receipt.Amount),
                Bold: true,
                DoubleHeight: true,
                DoubleWidth: true),
        };

        nodes.Add(new ReceiptNode.TextLine(
            receipt.ExpiresOn is { } expiresOn
                ? "Expires " + expiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "No expiry"));

        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.TextLine("Present this note, or its number, to redeem."));

        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.Barcode(receipt.CreditNoteNumber));

        // Never a cash payout - store credit does not open the drawer (contrast
        // EscPosReturnReceiptRenderer's Kick for a cash refund).
        nodes.Add(new ReceiptNode.Cut());

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <summary>
    /// Money as it reads on a receipt. Formatting only - it does not round, because the amount
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
