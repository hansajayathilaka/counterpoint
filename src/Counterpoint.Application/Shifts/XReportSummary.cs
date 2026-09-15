using System;
using System.Collections.Generic;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// A mid-shift, non-clearing snapshot: sales, returns, discounts, tax, tenders, cash movements and
/// the expected drawer, for one shift, right now (task P3-T02 "Do this" #1, SRS FR-8.3, RPT-04).
/// </summary>
/// <remarks>
/// This is the same object a future on-screen X report binds to and the one
/// <c>Counterpoint.Application.Abstractions.Devices.IXReportReceiptRenderer.Render</c> turns into a
/// printed slip - one read model, so the screen and the paper can never disagree about what an X
/// report says (task P3-T02 has no Avalonia screen of its own, matching the P2-T02 through
/// P3-T01 precedent of a service/query layer with the screen deferred).
/// </remarks>
/// <param name="ShiftId">The shift this snapshot is for.</param>
/// <param name="ShiftNo">Its allocated number.</param>
/// <param name="UserId">Who opened it.</param>
/// <param name="CashierDisplayName">Their name, for the header.</param>
/// <param name="OpenedAt">When the shift opened.</param>
/// <param name="GeneratedAt">When this snapshot was taken.</param>
/// <param name="ShiftDuration"><see cref="GeneratedAt"/> minus <see cref="OpenedAt"/> - "current shift duration" (task P3-T02 "Do this" #1).</param>
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
/// <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/>'s answer for this shift
/// (<see cref="IExpectedCashService"/>) - what the drawer should hold right now.
/// </param>
public sealed record XReportSummary(
    long ShiftId,
    string ShiftNo,
    long UserId,
    string CashierDisplayName,
    DateTimeOffset OpenedAt,
    DateTimeOffset GeneratedAt,
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
    ExpectedCashSummary ExpectedCash);
