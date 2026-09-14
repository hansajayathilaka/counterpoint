using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>What <see cref="ICreditNoteRedeemer.RedeemAsync"/> hands back once a redemption has
/// committed (task P2-T05).</summary>
/// <param name="CreditNoteId">The <c>credit_note</c> row that was spent against.</param>
/// <param name="AmountRedeemed">What this one redemption took off the balance.</param>
/// <param name="RemainingBalance"><c>amount_remaining</c> after this redemption.</param>
/// <param name="FullySpent">
/// True when <see cref="RemainingBalance"/> reached zero and the note's <c>status</c> was flipped
/// to <c>SPENT</c> in the same write.
/// </param>
public sealed record RedeemedCreditNote(
    long CreditNoteId,
    Money AmountRedeemed,
    Money RemainingBalance,
    bool FullySpent);
