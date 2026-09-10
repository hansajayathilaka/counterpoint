using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A completed bill as the receipt needs it: what to print, in the shop's vocabulary, with no
/// escape code and no layout decision anywhere in sight.
/// </summary>
/// <remarks>
/// It carries no cost and no margin (CLAUDE.md invariant 8) - a customer receipt could hardly
/// be a worse place for either.
/// </remarks>
/// <param name="BillNo">The allocated bill number, printed and encoded in the barcode.</param>
/// <param name="SoldAt">When the bill was completed.</param>
/// <param name="Lines">The bill lines, in order.</param>
/// <param name="Subtotal">Sum of the line totals.</param>
/// <param name="Discount">Line and bill discount combined (SRS §10.1's "Discount" row).</param>
/// <param name="TaxableValue"><c>Subtotal - Discount</c> - the base tax was charged on.</param>
/// <param name="Tax">Tax on the bill.</param>
/// <param name="Total">What the customer pays.</param>
/// <param name="Tenders">How they paid.</param>
/// <param name="Change">
/// What was handed back on an over-tender. Zero for a reprint of a historical bill: the change
/// given was never a stored value (only the <em>applied</em> tender is, SRS FR-3.26) and cannot
/// be recovered from <c>payment</c> rows after the fact - a deliberate, documented limit, not a
/// bug (CLAUDE.md invariant 4's cousin: nothing here re-derives a durable fact from a bigger one).
/// </param>
/// <param name="TaxBreakdown">One row per tax rate the bill's lines actually used.</param>
/// <param name="CashierName">The seller's display name (SRS §10.1, <c>ReceiptSettings.ShowCashierName</c>).</param>
/// <param name="CustomerName">
/// The named customer, or <c>Walk-in</c> when the sale carries no <c>customer_id</c>.
/// </param>
/// <param name="IsTradeCustomer">
/// Whether the named customer is a trade account - the A4 invoice's signature area is for these
/// (PRT-09), not for a walk-in receipt.
/// </param>
public sealed record SaleReceipt(
    string BillNo,
    DateTimeOffset SoldAt,
    IReadOnlyList<SaleReceiptLine> Lines,
    Money Subtotal,
    Money Discount,
    Money TaxableValue,
    Money Tax,
    Money Total,
    IReadOnlyList<SaleReceiptTender> Tenders,
    Money Change,
    IReadOnlyList<SaleReceiptTaxLine> TaxBreakdown,
    string CashierName,
    string CustomerName,
    bool IsTradeCustomer);
