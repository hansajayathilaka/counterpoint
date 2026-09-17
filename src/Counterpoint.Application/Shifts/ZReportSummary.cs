using System;
using System.Collections.Generic;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// The full totals a Z report closes a shift with (task P3-T03 "Do this" #1-2, SRS FR-8.4).
/// </summary>
/// <remarks>
/// <para>
/// Composed from the same reads <see cref="XReportSummary"/> already is -
/// <c>IXReportFiguresReader</c>, <c>ICashMovementReader</c> and <c>IExpectedCashService</c>
/// (P3-T01's single expected-cash formula) - plus the close itself: the physical count, the
/// variance against expected, who closed it, and the note. This is the one read model both the
/// printed Z report and <see cref="ClosedShift"/>'s own result bind to, so the screen (when one is
/// built) and the paper can never disagree about what happened at close.
/// </para>
/// <para>
/// Unlike <see cref="XReportSummary"/>, this is never recomputed on demand - it describes one
/// close, taken exactly once (SRS FR-8.8), and is built from the figures gathered at the moment
/// <see cref="ICloseShift.CloseAsync"/> ran.
/// </para>
/// </remarks>
/// <param name="ShiftId">The shift that was closed.</param>
/// <param name="ShiftNo">Its allocated number.</param>
/// <param name="UserId">Who opened it.</param>
/// <param name="CashierDisplayName">Their name, for the header.</param>
/// <param name="OpenedAt">When the shift opened.</param>
/// <param name="ClosedAt">When it closed.</param>
/// <param name="ShiftDuration"><see cref="ClosedAt"/> minus <see cref="OpenedAt"/>.</param>
/// <param name="OpeningFloat">The cash counted in before trading started.</param>
/// <param name="SalesCount">Completed sales rung up this shift.</param>
/// <param name="SalesValue">Their total value.</param>
/// <param name="ReturnsCount">Returns taken this shift.</param>
/// <param name="ReturnsValue">Their total refund value.</param>
/// <param name="DiscountTotal">Line and bill discount given on this shift's own sales.</param>
/// <param name="SalesTaxTotal">Tax collected on this shift's own sales.</param>
/// <param name="ReturnsTaxTotal">Tax given back on this shift's own returns.</param>
/// <param name="TaxBreakdown">Sales tax collected this shift, broken down by rate.</param>
/// <param name="Tenders">Sales and refunds this shift, broken down by tender type.</param>
/// <param name="CashMovements">Every cash-in and cash-out recorded against this shift, newest first.</param>
/// <param name="ExpectedCash">
/// What the drawer should hold, per <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/>,
/// at the moment of closing.
/// </param>
/// <param name="CountedCash">What the cashier physically counted.</param>
/// <param name="Variance"><see cref="CountedCash"/> minus <see cref="ExpectedCash"/> - over (positive) or short (negative).</param>
/// <param name="ClosedByUserId">Who actually closed it - not always <see cref="UserId"/> (SRS FR-8.7).</param>
/// <param name="ClosedByDisplayName">Their name, for the header.</param>
/// <param name="Note">The variance note, mandatory once <see cref="Variance"/> exceeds the configured threshold.</param>
public sealed record ZReportSummary(
    long ShiftId,
    string ShiftNo,
    long UserId,
    string CashierDisplayName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    TimeSpan ShiftDuration,
    Money OpeningFloat,
    int SalesCount,
    Money SalesValue,
    int ReturnsCount,
    Money ReturnsValue,
    Money DiscountTotal,
    Money SalesTaxTotal,
    Money ReturnsTaxTotal,
    IReadOnlyList<XReportTaxBreakdownLine> TaxBreakdown,
    IReadOnlyList<XReportTenderLine> Tenders,
    IReadOnlyList<CashMovementRecord> CashMovements,
    Money ExpectedCash,
    Money CountedCash,
    Money Variance,
    long ClosedByUserId,
    string ClosedByDisplayName,
    string? Note);
