using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Turns a Z report into ESC/POS bytes, through the receipt IR and <see cref="EscPosRenderer"/>
/// (SRS FR-7.1, FR-8.4, task P3-T03 "Do this" #2).
/// </summary>
/// <remarks>
/// The same fixed layout <see cref="EscPosXReportRenderer"/> uses, extended with the physical
/// count, the variance and the note a Z report closes with - deliberately not the owner-editable
/// Scriban template engine (P1-T11's), which is wired to the sales bill only. Pure - no device, no
/// file, no clock - and safe to call from inside the close transaction (CLAUDE.md invariant 7:
/// rendering bytes is not a printer call).
/// </remarks>
public sealed class EscPosZReportRenderer : IZReportReceiptRenderer
{
    private readonly EscPosRenderer _renderer;
    private readonly IRoundingPolicy _rounding;

    /// <summary>Creates the renderer.</summary>
    /// <param name="renderer">The ESC/POS byte renderer for the shop's printer.</param>
    /// <param name="rounding">
    /// The shop's rounding rule. Used only to decide how many decimal places to print - every
    /// amount on a Z report is already the shop's own stored figure (CLAUDE.md invariant 2).
    /// </param>
    public EscPosZReportRenderer(EscPosRenderer renderer, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(rounding);

        _renderer = renderer;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public byte[] Render(ZReportSummary report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("Z REPORT", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.TextLine("(shift closed - final)", TextAlign.Centre),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Shift    : " + report.ShiftNo),
            new ReceiptNode.TextLine("Cashier  : " + report.CashierDisplayName),
            new ReceiptNode.TextLine("Opened   : " + FormatTimestamp(report.OpenedAt)),
            new ReceiptNode.TextLine("Closed   : " + FormatTimestamp(report.ClosedAt)),
            new ReceiptNode.TextLine("Closed by: " + report.ClosedByDisplayName),
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
        nodes.Add(new ReceiptNode.Columns("Opening float", FormatAmount(report.OpeningFloat)));
        nodes.Add(new ReceiptNode.Columns("Expected cash", FormatAmount(report.ExpectedCash)));
        nodes.Add(new ReceiptNode.Columns("Counted cash", FormatAmount(report.CountedCash), Bold: true));
        nodes.Add(new ReceiptNode.Columns(
            "Variance", FormatVariance(report.Variance), Bold: true, DoubleHeight: true));

        if (!string.IsNullOrWhiteSpace(report.Note))
        {
            nodes.Add(new ReceiptNode.TextLine("Note: " + report.Note));
        }

        nodes.Add(new ReceiptNode.Divider());
        nodes.Add(new ReceiptNode.TextLine("Shift closed - locked. No further", TextAlign.Centre));
        nodes.Add(new ReceiptNode.TextLine("transactions may post to this shift.", TextAlign.Centre));

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
    /// never "-0.00" for a figure that did not occur this shift.
    /// </summary>
    private string FormatDeduction(Money amount) =>
        amount.IsPositive ? "-" + FormatAmount(amount) : FormatAmount(amount);

    /// <summary>Over reads with a plus, short reads with a minus, so the sign is never ambiguous on paper.</summary>
    private string FormatVariance(Money variance) =>
        variance.IsPositive ? "+" + FormatAmount(variance) : FormatAmount(variance);

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
