using System.Collections.Generic;

namespace Counterpoint.Devices.Printing.Templates;

/// <summary>
/// What a receipt template renders against - shop profile, bill, lines, tenders, tax breakdown,
/// policy text, cashier, date and the duplicate flag (P1-T11's own list of what the template
/// model must carry).
/// </summary>
/// <remarks>
/// <para>
/// Every amount is already formatted text, not a <c>Money</c> or a <c>decimal</c>: the two
/// rounding points are <c>IRoundingPolicy</c>, applied once, in
/// <c>Counterpoint.Application.Sales.CompleteSaleHandler</c> (CLAUDE.md invariant 2), and this
/// model exists only to be printed. A template that could still do arithmetic on an amount would
/// be a third place rounding could happen.
/// </para>
/// <para>
/// Property names are PascalCase; Scriban's default member renamer turns them into the
/// snake_case a template writes (<c>shop.name</c>, <c>bill.bill_no</c>). See
/// <c>Counterpoint.Application.Settings.ReceiptTemplateDefaults</c> for the field list a template
/// can actually use.
/// </para>
/// </remarks>
public sealed class ReceiptTemplateModel
{
    public required ReceiptTemplateShop Shop { get; init; }

    public required ReceiptTemplateBill Bill { get; init; }

    public required IReadOnlyList<ReceiptTemplateLine> Lines { get; init; }

    public required IReadOnlyList<ReceiptTemplateTender> Tenders { get; init; }

    public required IReadOnlyList<ReceiptTemplateTaxLine> TaxBreakdown { get; init; }

    public required string PolicyText { get; init; }

    public required string HeaderText { get; init; }

    public required string FooterText { get; init; }

    public required string CashierName { get; init; }

    public required string CustomerName { get; init; }

    /// <summary>Already formatted in the shop's own date convention.</summary>
    public required string Date { get; init; }

    public required string Time { get; init; }

    /// <summary>True on a reprint (SRS FR-7.5, FR-7.6).</summary>
    public required bool IsDuplicate { get; init; }

    public required bool ShowLogo { get; init; }

    public required bool ShowBillBarcode { get; init; }

    public required bool ShowCashierName { get; init; }

    public required bool ShowCustomerName { get; init; }

    public required bool ShowItemAndUnitCount { get; init; }

    public required bool ShowTaxSummary { get; init; }

    public required bool ShowTaxableValue { get; init; }

    public required bool ShowTaxRegistrationNumber { get; init; }

    public required int ItemCount { get; init; }

    /// <summary>The sum of every line's quantity, regardless of unit - a tally, not arithmetic.</summary>
    public required string UnitsTotal { get; init; }
}

/// <summary>The shop profile a template's header prints from (SRS FR-10.1).</summary>
public sealed class ReceiptTemplateShop
{
    public required string Name { get; init; }

    public required string AddressLine1 { get; init; }

    public required string AddressLine2 { get; init; }

    public required string Phone { get; init; }

    public required string TaxRegistrationNumber { get; init; }
}

/// <summary>The bill header and totals a template prints (SRS §10.1).</summary>
public sealed class ReceiptTemplateBill
{
    public required string BillNo { get; init; }

    public required string Subtotal { get; init; }

    public required string Discount { get; init; }

    /// <summary>True when a discount was actually applied - the "Discount" row is data driven.</summary>
    public required bool HasDiscount { get; init; }

    public required string TaxableValue { get; init; }

    public required string Tax { get; init; }

    public required string Total { get; init; }

    public required string Change { get; init; }

    /// <summary>
    /// True when there is a change figure to show. False on a reprint of a historical bill: the
    /// change given was never a stored value (SRS FR-3.26) and cannot be recovered.
    /// </summary>
    public required bool HasChange { get; init; }

    /// <summary>True when a cash tender is on the bill - the drawer only opens for one (SRS FR-7.7).</summary>
    public required bool HasCashTender { get; init; }
}

/// <summary>One printed bill line (SRS §10.1).</summary>
public sealed class ReceiptTemplateLine
{
    public required string Description { get; init; }

    /// <summary>Quantity and unit together, for example <c>20 pcs</c>.</summary>
    public required string QuantityText { get; init; }

    public required string Rate { get; init; }

    public required string Amount { get; init; }
}

/// <summary>One printed tender line.</summary>
public sealed class ReceiptTemplateTender
{
    public required string TenderType { get; init; }

    public required string Amount { get; init; }
}

/// <summary>One row of the tax breakdown (SRS §10.1's "Tax @ n%" row).</summary>
public sealed class ReceiptTemplateTaxLine
{
    public required string Label { get; init; }

    public required string TaxableAmount { get; init; }

    public required string TaxAmount { get; init; }
}
