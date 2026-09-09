using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// What the bill on the screen currently comes to (SRS FR-3.10).
/// </summary>
/// <remarks>
/// The same arithmetic that will run at completion, so the figure the cashier reads out is the
/// figure that gets charged and tendered. Nothing about it is provisional except that the bill
/// is not saved yet.
/// </remarks>
/// <param name="Lines">The priced lines, in order.</param>
/// <param name="Subtotal">Sum of the line totals, before <see cref="BillDiscount"/>.</param>
/// <param name="BillDiscount">The whole-bill discount, if the cashier asked for one (SRS FR-3.17).</param>
/// <param name="Tax">Tax on the bill.</param>
/// <param name="Total">What the customer will pay.</param>
/// <param name="Warnings">
/// Plain-language warnings about the bill as quoted - today, only a negative-stock warning
/// (SRS FR-3.13) when the shop's policy is "warn and allow". Shown before payment, not only
/// after; empty when there is nothing to say.
/// </param>
public sealed record SaleQuote(
    IReadOnlyList<QuotedLine> Lines,
    Money Subtotal,
    Money BillDiscount,
    Money Tax,
    Money Total,
    IReadOnlyList<string> Warnings);
