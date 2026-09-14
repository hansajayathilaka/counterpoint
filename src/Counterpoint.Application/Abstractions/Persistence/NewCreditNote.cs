using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A credit note about to be issued, out of a return, with its number already allocated from
/// <c>number_sequence</c> in the same transaction (SRS FR-5 store credit, task P2-T05).
/// </summary>
/// <param name="Number">Allocated from <c>number_sequence</c>, doc_type <c>CREDIT_NOTE</c>.</param>
/// <param name="SaleReturnId">The return this credit note was issued from. Always given - a
/// credit note with no return behind it is not something this task creates.</param>
/// <param name="CustomerId">
/// The customer it is issued to, if known - inherited from the return, never required. A credit
/// note with no customer is still redeemable by anyone presenting the number (SRS FR-5, task
/// P2-T05's "keep it to a numbered voucher with a balance").
/// </param>
/// <param name="Amount">
/// What the note is issued for. <c>amount_issued</c> and <c>amount_remaining</c> both start here -
/// a freshly issued note has never been spent.
/// </param>
/// <param name="IssuedAt">When the note was issued.</param>
/// <param name="ExpiresOn">
/// The last business date the note may be redeemed on, or null to never expire. There is no shop
/// wide default expiry policy in <c>PolicySettings</c> (task P2-T05: the SRS does not ask for one),
/// so this is exactly what the caller passes - optional, not defaulted.
/// </param>
public sealed record NewCreditNote(
    string Number,
    long SaleReturnId,
    long? CustomerId,
    Money Amount,
    DateTimeOffset IssuedAt,
    DateOnly? ExpiresOn);
