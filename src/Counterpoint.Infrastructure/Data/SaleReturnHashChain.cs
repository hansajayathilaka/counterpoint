using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The canonical form of a <c>sale_return</c> row, and its place in the chain (docs/01_DATA_MODEL.md
/// §6, CLAUDE.md invariant 6's return-side cousin, task P2-T02).
/// </summary>
/// <remarks>
/// <para>
/// Same rules as <see cref="SaleHashChain"/>: the <c>CREATE TABLE sale_return</c> column order of
/// docs/01_DATA_MODEL.md §6, written out by hand, less <c>id</c>, <c>prev_hash</c> and
/// <c>row_hash</c> for the same reasons <see cref="SaleHashChain"/> excludes <c>sale</c>'s. Two
/// chains on two different tables, one definition of what a link is.
/// </para>
/// <para>
/// Unlike <c>sale</c>, <c>sale_return</c> has no column-scoped update exception at all
/// (docs/01_DATA_MODEL.md §8: "no correcting update to a return - a mistake is fixed with another
/// document"), so every column the table has is chained here. This is its own chain, walked in its
/// own <c>id</c> order - not a continuation of <c>sale</c>'s.
/// </para>
/// <para>
/// <b>This order is a published format.</b> Changing it invalidates every hash ever written, so a
/// column added to <c>sale_return</c> in a later migration is appended here, never inserted in the
/// middle.
/// </para>
/// </remarks>
internal static class SaleReturnHashChain
{
    /// <summary>The canonical JSON of one return.</summary>
    internal static string Canonicalise(SaleReturn saleReturn) => new CanonicalJson()
        .Add("return_no", saleReturn.ReturnNo)
        .Add("returned_at", saleReturn.ReturnedAt)
        .Add("business_date", saleReturn.BusinessDate)
        .Add("original_sale_id", saleReturn.OriginalSaleId)
        .Add("exchange_sale_id", saleReturn.ExchangeSaleId)
        .Add("customer_id", saleReturn.CustomerId)
        .Add("user_id", saleReturn.UserId)
        .Add("shift_id", saleReturn.ShiftId)
        .Add("subtotal", saleReturn.Subtotal)
        .Add("tax", saleReturn.Tax)
        .Add("restocking_fee", saleReturn.RestockingFee)
        .Add("total_refund", saleReturn.TotalRefund)
        .Add("refund_method", saleReturn.RefundMethod)
        .Add("authorised_by", saleReturn.AuthorisedBy)
        .Add("reason", saleReturn.Reason)
        .ToString();

    /// <summary>The row hash of one return, given its predecessor's.</summary>
    internal static string RowHash(string previousHash, SaleReturn saleReturn) =>
        HashChain.Compute(previousHash, Canonicalise(saleReturn));

    /// <summary>True when a stored return still hashes to what it claims.</summary>
    internal static bool Verify(SaleReturn saleReturn) =>
        HashChain.Verify(saleReturn.PrevHash, Canonicalise(saleReturn), saleReturn.RowHash);
}
