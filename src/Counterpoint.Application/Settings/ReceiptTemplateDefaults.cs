namespace Counterpoint.Application.Settings;

/// <summary>
/// The Scriban template body a fresh shop trades on, before the owner has changed a word of it
/// (SRS FR-7.3, FR-10.8, §10.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>This file holds text, not code.</b> It has no dependency on Scriban or on
/// <c>Counterpoint.Devices</c> - only the rendering engine in <c>Counterpoint.Devices.Printing.Templates</c>
/// knows what a <c>{{ }}</c> tag means. What lives here is exactly what the owner would type into
/// the settings screen: the wording and layout of the §10.1 specimen bill, expressed in the small
/// line-directive language <c>ReceiptDirectiveParser</c> turns into the receipt IR
/// (<c>Counterpoint.Devices.Printing.ReceiptNode</c>).
/// </para>
/// <para>
/// <b>The directive language, one line at a time:</b>
/// </para>
/// <list type="bullet">
///   <item><c>TEXT|align|bold|double|text</c> - one printed line. <c>align</c> is <c>L</c>, <c>C</c>
///   or <c>R</c>; <c>bold</c> and <c>double</c> are <c>0</c> or <c>1</c>.</item>
///   <item><c>COLS|left|right|bold|double</c> - a description on the left, an amount on the right.</item>
///   <item><c>DIV</c> - a full-width rule.</item>
///   <item><c>BARCODE|data</c> / <c>QR|data</c> - the bill number, scannable (SRS FR-7.4, PRT-04).</item>
///   <item><c>FEED|n</c> - blank vertical space.</item>
///   <item><c>CUT</c> - cuts the paper (PRT-05); <c>KICK</c> - opens the drawer (FR-7.7).</item>
///   <item>Any other line prints exactly as written, left aligned - so a template that is nothing
///   but plain text (an owner who only wants to change a word of the footer) still works with no
///   directives at all.</item>
/// </list>
/// <para>
/// The model this template renders against is documented on
/// <c>Counterpoint.Devices.Printing.Templates.ReceiptTemplateModel</c>. The field names below
/// (<c>shop.name</c>, <c>bill.bill_no</c>, and so on) are that model's public properties,
/// snake-cased the way Scriban's default member renamer expects.
/// </para>
/// </remarks>
public static class ReceiptTemplateDefaults
{
    /// <summary>
    /// The §10.1 specimen sales bill, as Scriban source. Ships as
    /// <see cref="SettingDefaults.Receipt"/>'s <c>TemplateText</c> and is what a shop trades on
    /// until the owner edits it in Settings - no rebuild, no redeploy (NFR-M1).
    /// </summary>
    public const string SalesBillTemplate =
        """
        {{~ if show_logo ~}}
        TEXT|C|0|0|[ SHOP LOGO ]
        DIV
        {{~ end ~}}
        {{~ if is_duplicate ~}}
        TEXT|C|1|1|DUPLICATE - NOT A VALID ORIGINAL
        {{~ end ~}}
        TEXT|C|1|1|{{ shop.name }}
        {{~ if header_text != "" ~}}
        TEXT|C|0|0|{{ header_text }}
        {{~ end ~}}
        {{~ if shop.address_line1 != "" ~}}
        TEXT|C|0|0|{{ shop.address_line1 }}
        {{~ end ~}}
        {{~ if shop.address_line2 != "" ~}}
        TEXT|C|0|0|{{ shop.address_line2 }}
        {{~ end ~}}
        {{~ if shop.phone != "" ~}}
        TEXT|C|0|0|Tel: {{ shop.phone }}
        {{~ end ~}}
        {{~ if show_tax_registration_number && shop.tax_registration_number != "" ~}}
        TEXT|C|0|0|Tax Reg No: {{ shop.tax_registration_number }}
        {{~ end ~}}
        DIV
        TEXT|L|0|0|Bill No : {{ bill.bill_no }}
        TEXT|L|0|0|Date    : {{ date }}   Time: {{ time }}
        {{~ if show_cashier_name ~}}
        TEXT|L|0|0|Cashier : {{ cashier_name }}
        {{~ end ~}}
        {{~ if show_customer_name ~}}
        TEXT|L|0|0|Customer: {{ customer_name }}
        {{~ end ~}}
        DIV
        TEXT|L|0|0|Item                Qty   Rate    Amount
        DIV
        {{~ for line in lines ~}}
        TEXT|L|0|0|{{ line.description }}
        COLS|  {{ line.quantity_text }} @ {{ line.rate }}|{{ line.amount }}|0|0
        {{~ end ~}}
        DIV
        COLS|Sub total|{{ bill.subtotal }}|0|0
        {{~ if bill.has_discount ~}}
        COLS|Discount|-{{ bill.discount }}|0|0
        {{~ end ~}}
        {{~ if show_taxable_value ~}}
        COLS|Taxable value|{{ bill.taxable_value }}|0|0
        {{~ end ~}}
        {{~ if show_tax_summary ~}}
        {{~ for t in tax_breakdown ~}}
        COLS|{{ t.label }}|{{ t.tax_amount }}|0|0
        {{~ end ~}}
        {{~ end ~}}
        DIV
        COLS|TOTAL|{{ bill.total }}|1|1
        DIV
        {{~ for tender in tenders ~}}
        COLS|{{ tender.tender_type }}|{{ tender.amount }}|0|0
        {{~ end ~}}
        {{~ if bill.has_change ~}}
        COLS|CHANGE|{{ bill.change }}|0|0
        {{~ end ~}}
        DIV
        {{~ if show_item_and_unit_count ~}}
        TEXT|L|0|0|Items: {{ item_count }}     Units: {{ units_total }}
        {{~ end ~}}
        FEED|1
        {{~ if show_bill_barcode ~}}
        BARCODE|{{ bill.bill_no }}
        {{~ end ~}}
        FEED|1
        {{~ if policy_text != "" ~}}
        TEXT|L|0|0|{{ policy_text }}
        {{~ end ~}}
        {{~ if footer_text != "" ~}}
        TEXT|C|0|0|{{ footer_text }}
        {{~ end ~}}
        DIV
        {{~ if bill.has_cash_tender ~}}
        KICK
        {{~ end ~}}
        CUT
        """;
}
