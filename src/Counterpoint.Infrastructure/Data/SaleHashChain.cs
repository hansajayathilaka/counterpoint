using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The canonical form of a <c>sale</c> row, and its place in the chain (CLAUDE.md invariant 6).
/// </summary>
/// <remarks>
/// <para>
/// The field order below is the <c>CREATE TABLE sale</c> column order in
/// docs/01_DATA_MODEL.md §5, written out by hand. Six columns are absent and each for a reason.
/// </para>
/// <para>
/// <c>id</c> is assigned by the very insert this hash is part of, so it cannot be known while
/// the hash is being computed - the chain's order comes from <c>prev_hash</c>, not from the
/// column. <c>row_hash</c> is the output. <c>prev_hash</c> is already the concatenation's
/// prefix, so hashing it here as well would add nothing.
/// </para>
/// <para>
/// <c>status</c>, <c>cancelled_by</c> and <c>cancelled_at</c> are the three columns a
/// cancellation is allowed to change (CLAUDE.md invariant 5), and a hash over a column that may
/// legitimately change would fail to verify on every cancelled bill - which would make the
/// chain worthless precisely where it is most wanted. What is chained here is the bill's
/// immutable content: who sold what, for how much, under which number. That a bill was
/// cancelled is evidence in its own right, and it is carried by the <c>audit_log</c> chain,
/// which has no mutable column at all.
/// </para>
/// <para>
/// <b>This order is a published format.</b> Changing it invalidates every hash ever written, so
/// a column added to <c>sale</c> in a later migration is appended here, never inserted in the
/// middle, and the change needs a chain rebuild plan of its own.
/// </para>
/// </remarks>
internal static class SaleHashChain
{
    /// <summary>The canonical JSON of one bill.</summary>
    internal static string Canonicalise(Sale sale) => new CanonicalJson()
        .Add("bill_no", sale.BillNo)
        .Add("sold_at", sale.SoldAt)
        .Add("business_date", sale.BusinessDate)
        .Add("customer_id", sale.CustomerId)
        .Add("user_id", sale.UserId)
        .Add("shift_id", sale.ShiftId)
        .Add("subtotal", sale.Subtotal)
        .Add("line_discount", sale.LineDiscount)
        .Add("bill_discount", sale.BillDiscount)
        .Add("tax", sale.Tax)
        .Add("rounding", sale.Rounding)
        .Add("total", sale.Total)
        .Add("cogs", sale.Cogs)
        .Add("note", sale.Note)
        .ToString();

    /// <summary>The row hash of one bill, given its predecessor's.</summary>
    internal static string RowHash(string previousHash, Sale sale) =>
        HashChain.Compute(previousHash, Canonicalise(sale));

    /// <summary>True when a stored bill still hashes to what it claims.</summary>
    internal static bool Verify(Sale sale) =>
        HashChain.Verify(sale.PrevHash, Canonicalise(sale), sale.RowHash);
}
