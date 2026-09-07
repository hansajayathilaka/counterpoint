using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a completed bill into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// A fixed layout, deliberately. The owner-editable Scriban template, the shop header and
/// footer out of settings and the return-policy paragraph are all P1-T11; what this task needs
/// is the wire from a committed bill to a byte stream, and a layout that would have to be
/// thrown away is not worth writing twice.
/// </para>
/// <para>
/// Pure: no device, no file, no clock. It is therefore safe to call inside the sale
/// transaction, which is where the bill number becomes available (CLAUDE.md invariant 7 bans
/// the <i>printer call</i>, not the rendering).
/// </para>
/// </remarks>
public sealed class EscPosSaleReceiptRenderer : ISaleReceiptRenderer
{
    /// <summary>Tender type that opens the drawer (SRS FR-7.7).</summary>
    private const string CashTender = "CASH";

    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - the
    /// amounts arriving here are already rounded (CLAUDE.md invariant 2).
    /// </param>
    public EscPosSaleReceiptRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(SaleReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("SALES BILL", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Bill No : " + receipt.BillNo),
            new ReceiptNode.TextLine(
                "Date    : " + receipt.SoldAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new ReceiptNode.Divider(),
        };

        foreach (var line in receipt.Lines)
        {
            nodes.Add(new ReceiptNode.TextLine(line.Description));
            nodes.Add(new ReceiptNode.Columns(
                "  " + FormatQuantity(line) + " @ " + FormatAmount(line.UnitPrice),
                FormatAmount(line.LineTotal)));
        }

        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.Columns("Sub total", FormatAmount(receipt.Subtotal)));
        nodes.Add(new ReceiptNode.Columns("Tax", FormatAmount(receipt.Tax)));
        nodes.Add(new ReceiptNode.Columns(
            "TOTAL",
            FormatAmount(receipt.Total),
            Bold: true,
            DoubleHeight: true,
            DoubleWidth: true));
        nodes.Add(new ReceiptNode.Divider());

        var cashTendered = false;
        foreach (var tender in receipt.Tenders)
        {
            nodes.Add(new ReceiptNode.Columns(tender.TenderType, FormatAmount(tender.Amount)));
            cashTendered |= string.Equals(tender.TenderType, CashTender, StringComparison.Ordinal);
        }

        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.Barcode(receipt.BillNo));
        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.TextLine("Thank you - please come again", TextAlign.Centre));

        if (cashTendered)
        {
            // Only a cash tender opens the drawer. A card sale that popped it would be a
            // reconciliation problem, not a convenience.
            nodes.Add(new ReceiptNode.Kick());
        }

        nodes.Add(new ReceiptNode.Cut());

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <summary>
    /// Money as the customer reads it. Formatting only - it does not round, because the amounts
    /// were rounded at the two points that are allowed to.
    /// </summary>
    private string FormatAmount(Money amount)
    {
        var format = _rounding.DecimalPlaces > 0
            ? "0." + new string('0', _rounding.DecimalPlaces)
            : "0";

        return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(SaleReceiptLine line) =>
        line.Quantity.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + line.UomSymbol;
}
