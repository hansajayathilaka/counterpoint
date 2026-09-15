using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns an X report snapshot into ESC/POS bytes, through the receipt IR and
/// <see cref="EscPosRenderer"/> (SRS FR-7.1, FR-8.3, RPT-04, task P3-T02 "Do this" #2).
/// </summary>
/// <remarks>
/// A fixed layout, the same deliberate choice <see cref="EscPosCashSlipRenderer"/> made for a
/// cash slip: an X report is not the owner-editable Scriban template engine's (P1-T11's), which is
/// wired to the sales bill only. Pure - no device, no file, no clock - and never called from inside
/// a database transaction (CLAUDE.md invariant 7; there is none here to be inside of - task P3-T02's
/// own risk note is that an X report changes nothing).
///
/// <para>
/// No <see cref="ReceiptNode.Kick"/>: an X report is a read of the drawer's expected contents, not
/// a cash event, and <see cref="ReceiptNode.Kick"/>'s own remarks are explicit that "only a cash
/// tender or an authorised 'no sale' puts this on a document".
/// </para>
/// <para>
/// The cash-movement list itself is not itemised on the paper copy - only its totals, taken from
/// <see cref="XReportSummary.ExpectedCash"/> - to keep the printed slip bounded regardless of how
/// many cash movements a long shift has recorded; <see cref="XReportSummary.CashMovements"/>
/// carries the full, itemised list for a future on-screen report to page through.
/// </para>
/// </remarks>
public sealed class EscPosXReportRenderer : IXReportReceiptRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - every
    /// amount on an X report is already the shop's own stored figure (CLAUDE.md invariant 2).
    /// </param>
    public EscPosXReportRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(XReportSummary report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("X REPORT", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.TextLine("(mid-shift snapshot - not a close)", TextAlign.Centre),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Shift    : " + report.ShiftNo),
            new ReceiptNode.TextLine("Cashier  : " + report.CashierDisplayName),
            new ReceiptNode.TextLine("Opened   : " + FormatTimestamp(report.OpenedAt)),
            new ReceiptNode.TextLine("Printed  : " + FormatTimestamp(report.GeneratedAt)),
            new ReceiptNode.TextLine("Duration : " + FormatDuration(report.ShiftDuration)),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("SALES", Bold: true),
            new ReceiptNode.Columns("Count", report.SalesCount.ToString(CultureInfo.InvariantCulture)),
            new ReceiptNode.Columns("Value", FormatAmount(report.SalesValue)),
            new ReceiptNode.Columns("Discounts", FormatAmount(report.DiscountTotal)),
            new ReceiptNode.Columns("Tax", FormatAmount(report.SalesTaxTotal)),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("RETURNS", Bold: true),
            new ReceiptNode.Columns("Count", report.ReturnsCount.ToString(CultureInfo.InvariantCulture)),
            new ReceiptNode.Columns("Value", FormatAmount(report.ReturnsValue)),
            new ReceiptNode.Columns("Tax", FormatAmount(report.ReturnsTaxTotal)),
        };

        if (report.TaxBreakdown.Count > 0)
        {
            nodes.Add(new ReceiptNode.Divider());
            nodes.Add(new ReceiptNode.TextLine("TAX BREAKDOWN", Bold: true));

            foreach (var line in report.TaxBreakdown)
            {
                nodes.Add(new ReceiptNode.Columns(
                    line.Rate.ToString() + " on " + FormatAmount(line.TaxableAmount),
                    FormatAmount(line.TaxAmount)));
            }
        }

        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.TextLine("TENDERS", Bold: true));

        foreach (var tender in report.Tenders)
        {
            nodes.Add(new ReceiptNode.Columns(tender.TenderType + " sales", FormatAmount(tender.SalesAmount)));

            if (tender.RefundsAmount.IsPositive)
            {
                nodes.Add(new ReceiptNode.Columns(tender.TenderType + " refunds", FormatDeduction(tender.RefundsAmount)));
            }
        }

        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.TextLine("CASH DRAWER", Bold: true));
        nodes.Add(new ReceiptNode.Columns("Opening float", FormatAmount(report.ExpectedCash.OpeningFloat)));
        nodes.Add(new ReceiptNode.Columns("Cash sales", FormatAmount(report.ExpectedCash.CashSales)));
        nodes.Add(new ReceiptNode.Columns("Cash refunds", FormatDeduction(report.ExpectedCash.CashRefunds)));
        nodes.Add(new ReceiptNode.Columns("Cash in", FormatAmount(report.ExpectedCash.CashIn)));
        nodes.Add(new ReceiptNode.Columns("Cash out", FormatDeduction(report.ExpectedCash.CashOut)));
        nodes.Add(new ReceiptNode.Columns(
            "Expected cash", FormatAmount(report.ExpectedCash.ExpectedCash), Bold: true, DoubleHeight: true));

        nodes.Add(new ReceiptNode.Feed(1));
        nodes.Add(new ReceiptNode.Cut());

        return _renderer.Render(new ReceiptDocument(nodes));
    }

    /// <summary>Money as it reads on a receipt. Formatting only - it does not round (CLAUDE.md invariant 2).</summary>
    private string FormatAmount(Money amount)
    {
        var format = _rounding.DecimalPlaces > 0
            ? "0." + new string('0', _rounding.DecimalPlaces)
            : "0";

        return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A figure that reduces the drawer, prefixed with a minus so it reads as a subtraction - but
    /// never "-0.00" for a figure that did not occur this shift (no cash-out recorded, say).
    /// </summary>
    private string FormatDeduction(Money amount) =>
        amount.IsPositive ? "-" + FormatAmount(amount) : FormatAmount(amount);

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan duration)
    {
        var total = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)total.TotalHours}h {total.Minutes:00}m");
    }
}
