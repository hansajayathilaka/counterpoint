using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The shop-wide credit note reconciliation figure (task P2-T05 step 6, feeds a Phase 3 report
/// line): outstanding credit must equal issued minus redeemed.
/// </summary>
/// <param name="TotalIssued">Sum of every <c>credit_note.amount_issued</c> ever written.</param>
/// <param name="TotalRedeemed">Sum of every <c>credit_note_redemption.amount</c> ever written.</param>
/// <param name="TotalOutstanding">
/// Sum of every <c>credit_note.amount_remaining</c> as it stands right now. Identical to
/// <see cref="TotalIssued"/> minus <see cref="TotalRedeemed"/> by construction of the guarded
/// decrement <see cref="ICreditNoteRedeemer.RedeemAsync"/> performs - carried separately here so a
/// caller (or a test) can prove the identity against the stored rows rather than trust it.
/// </param>
public sealed record CreditNoteReconciliation(Money TotalIssued, Money TotalRedeemed, Money TotalOutstanding);
