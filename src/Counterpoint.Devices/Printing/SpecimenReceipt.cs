using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// The SRS §10.1 sales-bill specimen, hard-coded, as a <see cref="ReceiptDocument"/>.
///
/// <para>
/// This is the reference the ESC/POS renderer is snapshot-locked against: four lines
/// including a fractional quantity, a non-returnable annotation (PRT-06), a wrapped
/// return-policy paragraph (PRT-02), a double-height double-width total (PRT-03), a Code 128
/// bill-number barcode (PRT-04), a drawer kick on the cash tender (FR-7.7) and an auto-cut
/// (PRT-05). <c>HW-T01</c> prints exactly this on the shop's printer.
/// </para>
///
/// <para>
/// It is a specimen, not a template. Turning a real sale into an IR is P1-T11; nothing here is
/// reachable from the sale path. What it does share with the real thing is its settings: the
/// decimal places, the rounding rule, the tax rate and label, the return-policy paragraph and
/// the footer all come from <see cref="SettingsSnapshot"/> (FR-10.2, FR-10.3, FR-10.8), so
/// there is no rate, no limit and no currency symbol written into this file.
/// </para>
///
/// <para>
/// The shop identity in the header - name, address, telephone, tax registration number - is
/// specimen data, exactly like the four product lines and the cashier's name. It is the SRS
/// §10.1 example bill, not a default: a real shop's header comes from
/// <c>settings.Shop</c> in P1-T11, and printing a placeholder shop name on a real bill would be
/// worse than printing none.
/// </para>
///
/// <para>
/// The optional logo of the specimen is left out: it needs a real image, and the raster path
/// it would use is already proven by <see cref="ReceiptNode.RasterBarcode"/>.
/// </para>
/// </summary>
public static class SpecimenReceipt
{
    /// <summary>The bill number on the specimen, as printed and as encoded in the barcode.</summary>
    public const string BillNumber = "INV-2026-004312";

    /// <summary>Width of the item-description sub-column of an item row, in characters.</summary>
    private const int ItemColumnWidth = 14;

    /// <summary>Width of the quantity sub-column of an item row, in characters.</summary>
    private const int QuantityColumnWidth = 13;

    /// <summary>Width of the unit-price sub-column of an item row, in characters.</summary>
    private const int RateColumnWidth = 10;

    /// <summary>Pieces. A stand-in for the seeded <c>uom</c> rows P0-T06 introduces.</summary>
    private const long Pieces = 1;

    /// <summary>Metres.</summary>
    private const long Metres = 2;

    /// <summary>Services - the cutting charge.</summary>
    private const long Services = 3;

    /// <summary>
    /// Builds the specimen for 80 mm paper on the shop's default settings.
    /// </summary>
    public static ReceiptDocument Build() => Build(SettingDefaults.Snapshot);

    /// <summary>
    /// Builds the specimen for 80 mm paper under a given set of settings.
    /// </summary>
    /// <param name="settings">
    /// The shop's settings. The decimal places, the rounding rule, the tax rate and label, the
    /// return-policy paragraph and the footer all come from here - nothing about the money or the
    /// shop's wording is written into this file (SRS FR-10.2, FR-10.3, FR-10.8).
    /// </param>
    public static ReceiptDocument Build(SettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Build(
            RoundingPolicyFactory.Create(settings.Financial.RoundingRule, settings.Financial.DecimalPlaces),
            settings);
    }

    /// <summary>
    /// Builds the specimen for 80 mm paper under a given rounding policy, on default settings.
    /// </summary>
    /// <param name="rounding">
    /// The shop's rounding rule. Line totals and the bill total are the only two places it is
    /// applied (CLAUDE.md invariant 2).
    /// </param>
    public static ReceiptDocument Build(IRoundingPolicy rounding) =>
        Build(rounding, SettingDefaults.Snapshot);

    private static ReceiptDocument Build(IRoundingPolicy rounding, SettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(rounding);

        var lines = new SpecimenLine[]
        {
            new(
                "Hex Bolt M10x50 Zn",
                Quantity.FromDecimal(20m, Pieces),
                "pcs",
                Money.FromDecimal(25.00m),
                Note: null),
            new(
                "PVC Elbow 1\" 90deg",
                Quantity.FromDecimal(4m, Pieces),
                "pcs",
                Money.FromDecimal(90.00m),
                Note: null),
            new(
                "Cable 2.5mm 3-core",
                Quantity.FromDecimal(2.75m, Metres),
                "m",
                Money.FromDecimal(420.00m),
                Note: "(cut to length - non returnable)"),
            new(
                "Cutting charge",
                Quantity.FromDecimal(1m, Services),
                "svc",
                Money.FromDecimal(50.00m),
                Note: null),
        };

        var subTotal = Money.Zero;
        var unitCount = 0m;

        foreach (var line in lines)
        {
            subTotal += LineTotal(line, rounding);

            // A tally for the customer, not arithmetic: the units differ, and Quantity refuses
            // to add metres to pieces for exactly that reason. Hence plain decimal, and hence
            // it never leaves this receipt.
            unitCount += line.Quantity.Value;
        }

        var discount = Money.FromDecimal(65.00m);
        var valueOnTheBill = subTotal - discount;

        // From the shop's default tax class, not from a rate written into this file. A rate in
        // code is a wrong number printed on a bill the day the shop's regime changes (Q-02).
        //
        // Which way round the rate is applied is also the shop's, not this file's (FR-10.3). Under
        // tax-inclusive pricing - which is what the shipped defaults say - the line prices already
        // contain their tax, so it is carved out of the amount on the bill; adding it on top would
        // charge it twice. The net is taken as gross minus the tax rather than by dividing again,
        // so that the "Taxable value" and tax rows always add up to the total exactly.
        var taxRate = settings.Tax.DefaultTaxRate;
        var tax = settings.Tax.PricesIncludeTax
            ? taxRate.TaxWithinGross(valueOnTheBill)
            : taxRate.TaxOnNet(valueOnTheBill);

        var taxableValue = settings.Tax.PricesIncludeTax ? valueOnTheBill - tax : valueOnTheBill;
        var total = rounding.Round(taxableValue + tax);
        var cash = Money.FromDecimal(2500.00m);
        var change = cash - total;

        var nodes = new List<ReceiptNode>
        {
            new ReceiptNode.TextLine("SHOP NAME", TextAlign.Centre, Bold: true, DoubleHeight: true),
            new ReceiptNode.TextLine("123 Main Street, Town", TextAlign.Centre),
            new ReceiptNode.TextLine("Tel: 000-0000000", TextAlign.Centre),
            new ReceiptNode.TextLine("Tax Reg No: XXXXXXXXX", TextAlign.Centre),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine("Bill No : " + BillNumber),
            new ReceiptNode.TextLine("Date    : 03/09/2026   Time: 14:07"),
            new ReceiptNode.TextLine("Cashier : Kamal        Customer: Walk-in"),
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns(ItemRow("Item", "Qty", "Rate"), "Amount"),
            new ReceiptNode.Divider(),
        };

        foreach (var line in lines)
        {
            nodes.Add(new ReceiptNode.TextLine(line.Description));
            nodes.Add(new ReceiptNode.Columns(
                ItemRow(
                    string.Empty,
                    FormatQuantity(line.Quantity, line.UomSymbol),
                    FormatAmount(line.UnitPrice, rounding)),
                FormatAmount(LineTotal(line, rounding), rounding)));

            if (line.Note is not null)
            {
                nodes.Add(new ReceiptNode.TextLine("  " + line.Note));
            }
        }

        nodes.AddRange(
        [
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns("Sub total", FormatAmount(subTotal, rounding)),
            new ReceiptNode.Columns("Discount", FormatAmount(discount.Negate(), rounding)),
            new ReceiptNode.Columns("Taxable value", FormatAmount(taxableValue, rounding)),
            new ReceiptNode.Columns(TaxLabel(settings), FormatAmount(tax, rounding)),
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns(
                "TOTAL",
                FormatAmount(total, rounding),
                Bold: true,
                DoubleHeight: true,
                DoubleWidth: true),
            new ReceiptNode.Divider(),
            new ReceiptNode.Columns("Cash", FormatAmount(cash, rounding)),
            new ReceiptNode.Columns("CHANGE", FormatAmount(change, rounding)),
            new ReceiptNode.Divider(),
            new ReceiptNode.TextLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Items: {lines.Length}     Units: {unitCount.ToString("0.####", CultureInfo.InvariantCulture)}")),
            new ReceiptNode.Feed(1),
            new ReceiptNode.Barcode(BillNumber),
            new ReceiptNode.Feed(1),

            // The shop's own words, out of the receipt template (FR-10.8).
            new ReceiptNode.TextLine(settings.Receipt.PolicyText),
            new ReceiptNode.TextLine(settings.Receipt.FooterText, TextAlign.Centre),
            new ReceiptNode.Divider(),

            // Cash tender: the drawer opens. A card tender would not carry this node.
            new ReceiptNode.Kick(),
            new ReceiptNode.Cut(),
        ]);

        return new ReceiptDocument(nodes);
    }

    /// <summary>
    /// The tax row's label: the shop's word for tax and the rate it is charged at, both out of
    /// settings (FR-10.3). <c>Tax @ 0%</c> on the defaults, because the default class is exempt.
    /// </summary>
    private static string TaxLabel(SettingsSnapshot settings) => string.Concat(
        settings.Tax.TaxLabel,
        " @ ",
        settings.Tax.DefaultTaxRate.AsPercent.ToString("0.####", CultureInfo.InvariantCulture),
        "%");

    /// <summary>
    /// The line total: the one rounding point on a line (CLAUDE.md invariant 2).
    /// </summary>
    private static Money LineTotal(SpecimenLine line, IRoundingPolicy rounding) =>
        rounding.Round(line.UnitPrice * line.Quantity.Value);

    /// <summary>
    /// The item, quantity and rate sub-columns of an item row. The amount is not here: the
    /// renderer right-aligns that in the money column.
    /// </summary>
    private static string ItemRow(string item, string quantity, string rate) =>
        item.PadRight(ItemColumnWidth, ' ')
        + quantity.PadLeft(QuantityColumnWidth, ' ')
        + rate.PadLeft(RateColumnWidth, ' ');

    /// <summary>
    /// Money as the customer reads it: fixed decimal places, invariant culture.
    ///
    /// <para>
    /// Formatting only. It does not round (CLAUDE.md invariant 2): the two rounding points are
    /// <see cref="LineTotal"/> and the bill total, and every other amount printed here is
    /// already exact. Rounding again here would be a third point, and on the real sale path
    /// (P1-T11) it would silently mask an amount that arrived unrounded.
    /// </para>
    /// </summary>
    private static string FormatAmount(Money amount, IRoundingPolicy rounding)
    {
        var format = rounding.DecimalPlaces > 0
            ? "0." + new string('0', rounding.DecimalPlaces)
            : "0";

        return amount.Amount.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Quantity with its unit, trailing zeros trimmed: <c>20 pcs</c>, <c>2.75 m</c>.</summary>
    private static string FormatQuantity(Quantity quantity, string uomSymbol) =>
        quantity.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + uomSymbol;

    /// <summary>One line of the specimen bill.</summary>
    private sealed record SpecimenLine(
        string Description,
        Quantity Quantity,
        string UomSymbol,
        Money UnitPrice,
        string? Note);
}
