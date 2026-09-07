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
/// <param name="Tax">Tax on the bill.</param>
/// <param name="Total">What the customer pays.</param>
/// <param name="Tenders">How they paid.</param>
public sealed record SaleReceipt(
    string BillNo,
    DateTimeOffset SoldAt,
    IReadOnlyList<SaleReceiptLine> Lines,
    Money Subtotal,
    Money Tax,
    Money Total,
    IReadOnlyList<SaleReceiptTender> Tenders);
