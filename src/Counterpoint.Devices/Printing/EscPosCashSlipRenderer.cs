using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a cash movement or a no-sale drawer open into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/> (SRS FR-7.1, FR-7.7, task P3-T01).
/// </summary>
/// <remarks>
/// A fixed layout, the same deliberate choice <see cref="EscPosSaleCancellationReceiptRenderer"/>
/// made for a cancellation slip: neither of these two short tickets is the owner-editable Scriban
/// template engine's (P1-T11's), which is wired to the sales bill only. Pure - no device, no file,
/// no clock - so it is safe to call inside the cash-movement or no-sale transaction (CLAUDE.md
/// invariant 7).
/// </remarks>
public sealed class EscPosCashSlipRenderer : ICashSlipRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - the amount
    /// arriving here was already rounded, if at all, before this slip was requested (CLAUDE.md
    /// invariant 2).
    /// </param>
    public EscPosCashSlipRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] RenderCashMovementSlip(CashMovementSlip slip)
    {
        ArgumentNullException.ThrowIfNull(slip);

        var heading = slip.Direction == CashMovementDirection.In ? "CASH IN" : "CASH OUT";

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine(heading, TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Shift     : " + slip.ShiftNo),
            new ReceiptNode.TextLine(
                "Date      : " + slip.OccurredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine("By        : " + slip.CashierName),
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns("Amount", FormatAmount(slip.Amount), Bold: true, DoubleHeight: true, DoubleWidth: true),
            new ReceiptNode.TextLine("Reason: " + slip.Reason),
            new ReceiptNode.Feed(1),
            new ReceiptNode.Kick(),
            new ReceiptNode.Cut(),
        };

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <inheritdoc />
    public byte[] RenderNoSaleSlip(NoSaleSlip slip)
    {
        ArgumentNullException.ThrowIfNull(slip);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("NO SALE", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Shift       : " + slip.ShiftNo),
            new ReceiptNode.TextLine(
                "Date        : " + slip.OccurredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine("By          : " + slip.CashierName),
            new ReceiptNode.TextLine("Authorised  : " + slip.AuthorisedByName),
            new ReceiptNode.Feed(1),
            new ReceiptNode.Kick(),
            new ReceiptNode.Cut(),
        };

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <summary>
    /// Money as it reads on a receipt. Formatting only - it does not round (CLAUDE.md invariant 2).
    /// </summary>
    private string FormatAmount(Money amount)
    {
        var format = _rounding.DecimalPlaces > 0
            ? "0." + new string('0', _rounding.DecimalPlaces)
            : "0";

        return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
    }
}
