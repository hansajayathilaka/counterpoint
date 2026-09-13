using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The header of a return about to be written, with every total already computed and rounded
/// (task P2-T02).
/// </summary>
/// <param name="ReturnNo">Allocated from <c>number_sequence</c> in the same transaction.</param>
/// <param name="OriginalSaleId">
/// The bill this return is against, or null for an unlinked return (SRS FR-5.19, task P2-T03) -
/// <c>sale_return.original_sale_id</c> is nullable for exactly that case (docs/01_DATA_MODEL.md
/// §6). <c>CreateReturnHandler</c> (task P2-T02) always gives one; <c>CreateUnlinkedReturnHandler</c>
/// never does.
/// </param>
/// <param name="ReturnedAt">When the return was taken.</param>
/// <param name="BusinessDate">The trading day it belongs to.</param>
/// <param name="CustomerId">Inherited from the original sale, or null.</param>
/// <param name="UserId">The cashier taking it.</param>
/// <param name="ShiftId">The open shift it belongs to. A closed shift is refused by the database (AC-11).</param>
/// <param name="Subtotal">Sum of the line refunds (net, pre tax).</param>
/// <param name="Tax">Sum of the line tax refunds.</param>
/// <param name="RestockingFee">The policy's restocking fee, shown separately (task P2-T02 step 5).</param>
/// <param name="TotalRefund">
/// <c>Subtotal + Tax - RestockingFee</c>, exactly - not independently rounded, so this identity
/// holds over the stored row without a residual "rounding" column, which <c>sale_return</c> does
/// not carry (unlike <c>sale</c>).
/// </param>
/// <param name="RefundMethod">One of the <c>sale_return.refund_method</c> tokens.</param>
/// <param name="AuthorisedBy">The owner who granted an override, when one was needed.</param>
/// <param name="Reason">The return's own reason, if one line reason does not already say it all.</param>
/// <param name="ExchangeSaleId">
/// The new bill this return is paired with, or null for an ordinary return (SRS FR-5 exchange,
/// task P2-T04). Only <c>Counterpoint.Application.Exchanges.CreateExchangeHandler</c> ever gives
/// one - <c>Counterpoint.Application.Returns.CreateReturnHandler</c> and
/// <c>Counterpoint.Application.Returns.CreateUnlinkedReturnHandler</c> always leave it null,
/// because neither writes a paired sale for it to reference.
/// </param>
public sealed record NewSaleReturn(
    string ReturnNo,
    long? OriginalSaleId,
    DateTimeOffset ReturnedAt,
    DateOnly BusinessDate,
    long? CustomerId,
    long UserId,
    long ShiftId,
    Money Subtotal,
    Money Tax,
    Money RestockingFee,
    Money TotalRefund,
    string RefundMethod,
    long? AuthorisedBy,
    string? Reason,
    long? ExchangeSaleId = null);
