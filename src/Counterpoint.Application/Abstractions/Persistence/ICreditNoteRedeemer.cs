using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Spends a credit note against a sale, inside the sale's own transaction (SRS FR-5 store credit,
/// FR-3 tender, task P2-T05).
/// </summary>
/// <remarks>
/// <para>
/// <b>The guard lives in the write, not before it.</b> Task P2-T05's own risk note is
/// over-redemption through two partial redemptions racing; Counterpoint is single-user, so that
/// race cannot actually happen, but the guard belongs in the transaction regardless
/// (docs/01_DATA_MODEL.md §6): <c>UPDATE credit_note SET amount_remaining = amount_remaining -
/// :amt WHERE id = :id AND amount_remaining >= :amt</c>, checked by rows-affected, not a
/// read-then-write that trusts nothing changed in between.
/// </para>
/// <para>
/// <b>Expiry is evaluated here, at redemption, never by a background job</b> (task P2-T05 step 3).
/// <paramref name="businessDate"/> is passed in by the caller rather than read from a clock inside
/// this method, so a test - and a sale rung in after midnight against an earlier business day
/// (SRS's own "business date", not wall-clock date) - both compare the note's <c>expires_on</c>
/// against the same date the till itself is trading on.
/// </para>
/// </remarks>
public interface ICreditNoteRedeemer
{
    /// <summary>
    /// Redeems <paramref name="amount"/> off the credit note numbered <paramref name="number"/>.
    /// </summary>
    /// <param name="number">The credit note's own <c>number</c> - see <c>TenderTypes.CreditNote</c>
    /// for the convention that carries this from a sale's tender.</param>
    /// <param name="amount">How much of the bill this tender is settling. Always positive.</param>
    /// <param name="saleId">The sale this redemption is paying for.</param>
    /// <param name="redeemedAt">When the redemption happened.</param>
    /// <param name="businessDate">
    /// The sale's own business date - what <c>credit_note.expires_on</c> is compared against, not
    /// <see cref="DateTimeOffset.Now"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="InvalidOperationException">
    /// The note does not exist, is not <c>ACTIVE</c>, is expired as of <paramref name="businessDate"/>,
    /// or does not have <paramref name="amount"/> left to redeem.
    /// </exception>
    public Task<RedeemedCreditNote> RedeemAsync(
        string number,
        Money amount,
        long saleId,
        DateTimeOffset redeemedAt,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
