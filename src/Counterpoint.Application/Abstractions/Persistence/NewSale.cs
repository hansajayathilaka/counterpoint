using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The header of a bill about to be written, with every total already computed and rounded.
/// </summary>
/// <remarks>
/// The two rounding points (CLAUDE.md invariant 2) have both been applied by the time this
/// record exists: <see cref="Subtotal"/> is the sum of already-rounded line totals and
/// <see cref="Total"/> is the rounded bill total, with the difference recorded in
/// <see cref="Rounding"/>. Persistence rounds nothing.
/// </remarks>
/// <param name="BillNo">Allocated from <c>number_sequence</c> in the same transaction.</param>
/// <param name="SoldAt">When the bill was completed.</param>
/// <param name="BusinessDate">The trading day it belongs to - the grouping key for every rollup.</param>
/// <param name="UserId">Who rang it up.</param>
/// <param name="ShiftId">The open shift it belongs to.</param>
/// <param name="Subtotal">Sum of the line totals.</param>
/// <param name="LineDiscount">Sum of the line discounts.</param>
/// <param name="BillDiscount">Whole-bill discount, allocated across lines for reporting.</param>
/// <param name="Tax">Tax on the bill.</param>
/// <param name="Rounding">What the bill-total rounding moved.</param>
/// <param name="Total">What the customer pays.</param>
/// <param name="Cogs">Cost of the goods sold, snapshotted. Owner-only information.</param>
public sealed record NewSale(
    string BillNo,
    DateTimeOffset SoldAt,
    DateOnly BusinessDate,
    long UserId,
    long ShiftId,
    Money Subtotal,
    Money LineDiscount,
    Money BillDiscount,
    Money Tax,
    Money Rounding,
    Money Total,
    Money Cogs);
