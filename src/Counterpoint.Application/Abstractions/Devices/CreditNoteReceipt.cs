using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A freshly issued credit note as its printed document needs it (SRS FR-5 store credit, task
/// P2-T05 step 4). Carries no cost and no margin (CLAUDE.md invariant 8), the same as
/// <see cref="SaleReturnReceipt"/> - and, like that receipt, no customer name either: the return
/// this note was issued from never resolves one either (<see cref="SaleReturnReceipt"/>'s own
/// scope).
/// </summary>
/// <param name="CreditNoteNumber">The allocated credit note number, printed and encoded in the barcode.</param>
/// <param name="ReturnNo">The return this credit note was issued from.</param>
/// <param name="IssuedAt">When the note was issued.</param>
/// <param name="Amount">What the note is worth, freshly issued - <c>amount_issued</c>.</param>
/// <param name="ExpiresOn">The last business date it may be redeemed on, or null to never expire.</param>
/// <param name="CashierName">The seller's display name.</param>
public sealed record CreditNoteReceipt(
    string CreditNoteNumber,
    string ReturnNo,
    DateTimeOffset IssuedAt,
    Money Amount,
    DateOnly? ExpiresOn,
    string CashierName);
