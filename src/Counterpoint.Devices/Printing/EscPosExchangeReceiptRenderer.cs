using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a completed exchange into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/> (SRS FR-5 exchange, FR-7.1, task P2-T04).
/// </summary>
/// <remarks>
/// A fixed layout, the same deliberate choice <see cref="EscPosReturnReceiptRenderer"/> and
/// <see cref="EscPosSaleCancellationReceiptRenderer"/> made for their own documents: the
/// owner-editable Scriban template engine (P1-T11) is wired to the sales bill only. Pure - no
/// device, no file, no clock - so it is safe to call inside the exchange transaction (CLAUDE.md
/// invariant 7).
/// </remarks>
public sealed class EscPosExchangeReceiptRenderer : IExchangeReceiptRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - every
    /// amount arriving here was already rounded when the exchange was priced (CLAUDE.md invariant 2).
    /// </param>
    public EscPosExchangeReceiptRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(ExchangeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("EXCHANGE", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Return No : " + receipt.ReturnNo),
            new ReceiptNode.TextLine("Bill No   : " + receipt.BillNo),
            new ReceiptNode.TextLine("Orig. Bill: " + receipt.OriginalBillNo),
            new ReceiptNode.TextLine(
                "When      : " + receipt.ExchangedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.TextLine("By        : " + receipt.CashierName),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("RETURNED", Bold: true),
        };

        foreach (var line in receipt.ReturnedLines)
        {
            nodes.Add(new ReceiptNode.TextLine(
                string.Create(CultureInfo.InvariantCulture, $"{line.Description} ({line.Disposition})")));
            nodes.Add(new ReceiptNode.Columns(
                string.Create(CultureInfo.InvariantCulture, $"  {line.Quantity.Value} {line.UomSymbol} @ {FormatAmount(line.UnitPrice)}"),
                FormatAmount(line.LineRefund)));
        }

        if (receipt.RestockingFee.IsPositive)
        {
            nodes.Add(new ReceiptNode.Columns("Restocking fee", "-" + FormatAmount(receipt.RestockingFee)));
        }

        nodes.Add(new ReceiptNode.Columns("Return value", FormatAmount(receipt.ReturnValue), Bold: true));
        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.TextLine("REPLACEMENT", Bold: true));

        foreach (var line in receipt.ReplacementLines)
        {
            nodes.Add(new ReceiptNode.TextLine(line.Description));
            nodes.Add(new ReceiptNode.Columns(
                string.Create(CultureInfo.InvariantCulture, $"  {line.Quantity.Value} {line.UomSymbol} @ {FormatAmount(line.UnitPrice)}"),
                FormatAmount(line.LineTotal)));
        }

        nodes.Add(new ReceiptNode.Columns("Replacement value", FormatAmount(receipt.ReplacementTotal), Bold: true));
        nodes.Add(new ReceiptNode.Divider());

        if (receipt.CreditApplied.IsPositive)
        {
            nodes.Add(new ReceiptNode.Columns("Applied from return", "-" + FormatAmount(receipt.CreditApplied)));
        }

        if (receipt.AmountOwed.IsPositive)
        {
            nodes.Add(new ReceiptNode.Columns(
                "Amount due", FormatAmount(receipt.AmountOwed), Bold: true, DoubleHeight: true, DoubleWidth: true));

            foreach (var tender in receipt.Tenders)
            {
                nodes.Add(new ReceiptNode.Columns(tender.TenderType, FormatAmount(tender.Amount)));
            }

            if (receipt.Change.IsPositive)
            {
                nodes.Add(new ReceiptNode.Columns("Change", FormatAmount(receipt.Change)));
            }
        }
        else if (receipt.RefundPaid.IsPositive)
        {
            nodes.Add(new ReceiptNode.Columns(
                "Refunded to you", FormatAmount(receipt.RefundPaid), Bold: true, DoubleHeight: true, DoubleWidth: true));
            nodes.Add(new ReceiptNode.TextLine("Refunded by " + receipt.RefundMethod));
        }
        else
        {
            nodes.Add(new ReceiptNode.TextLine("Even exchange - nothing owed either way.", Bold: true));
        }

        if (!string.IsNullOrWhiteSpace(receipt.PolicyText))
        {
            nodes.Add(new ReceiptNode.Feed(1));
            nodes.Add(new ReceiptNode.TextLine(receipt.PolicyText));
        }

        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.Barcode(receipt.BillNo));

        // The drawer opens when cash actually moves either way - a cash tender for the
        // difference, or a cash refund of the surplus - the same reasoning
        // EscPosReturnReceiptRenderer and EscPosSaleReceiptRenderer each give for their own cash
        // case (SRS FR-7.7).
        var cashMoved = ContainsCashTender(receipt.Tenders)
            || (receipt.RefundPaid.IsPositive && string.Equals(receipt.RefundMethod, "CASH", StringComparison.Ordinal));

        if (cashMoved)
        {
            nodes.Add(new ReceiptNode.Kick());
        }

        nodes.Add(new ReceiptNode.Cut());

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    private static bool ContainsCashTender(IReadOnlyList<SaleReceiptTender> tenders)
    {
        foreach (var tender in tenders)
        {
            if (string.Equals(tender.TenderType, "CASH", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
