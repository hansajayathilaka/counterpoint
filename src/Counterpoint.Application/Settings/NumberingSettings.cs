using System.Collections.Generic;

namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.4 - the number series for every document the shop issues.
/// </summary>
/// <remarks>
/// <para>
/// These are the configured shape of each series. The live counter is
/// <c>number_sequence.next_val</c> and nothing here ever moves it backwards: a number, once
/// issued, is never reissued and a cancelled document keeps it (CLAUDE.md invariant 4, AC-19).
/// </para>
/// <para>
/// The defaults reproduce exactly what <c>FirstRunSeeder</c> (P0-T04/P0-T06) already writes, so
/// no bill, shift or test changes its number when the settings framework takes the series over.
/// <b>Q-16 is still unanswered</b>; when the shop answers it, the answer is a change to these
/// defaults and to the rows they seed, not a change to code.
/// </para>
/// </remarks>
/// <param name="Bill">Sales bills - <c>number_sequence.doc_type = 'SALE'</c>.</param>
/// <param name="Return">Returns - <c>'RETURN'</c>.</param>
/// <param name="CreditNote">Credit notes - <c>'CREDIT_NOTE'</c>.</param>
/// <param name="GoodsReceipt">Goods received notes - <c>'GRN'</c>.</param>
/// <param name="PurchaseOrder">Purchase orders - <c>'PO'</c>.</param>
/// <param name="Shift">
/// Shifts - <c>'SHIFT'</c>. Not named in FR-10.4, but the till cannot open one without a series
/// and leaving it out would mean a number the owner cannot see or change.
/// </param>
public sealed record NumberingSettings(
    DocumentNumbering Bill,
    DocumentNumbering Return,
    DocumentNumbering CreditNote,
    DocumentNumbering GoodsReceipt,
    DocumentNumbering PurchaseOrder,
    DocumentNumbering Shift)
{
    /// <summary>The <c>number_sequence.doc_type</c> of each series, paired with its settings.</summary>
    public IReadOnlyList<KeyValuePair<string, DocumentNumbering>> BySequence =>
    [
        new("SALE", Bill),
        new("RETURN", Return),
        new("CREDIT_NOTE", CreditNote),
        new("GRN", GoodsReceipt),
        new("PO", PurchaseOrder),
        new("SHIFT", Shift),
    ];
}
