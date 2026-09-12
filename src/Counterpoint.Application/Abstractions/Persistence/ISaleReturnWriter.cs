using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes the three tables a linked return touches: <c>sale_return</c>, <c>sale_return_line</c>
/// and the refund <c>payment</c> row - plus the one permitted update on <c>sale_line</c>
/// (task P2-T02, CLAUDE.md invariants 5 and 6).
/// </summary>
/// <remarks>
/// <para>
/// The same split <see cref="ISaleWriter"/> draws for a bill, for the same reason: the order is
/// part of the contract, and every method here joins the transaction already open on the caller's
/// flow. The hash chain on <c>sale_return</c> is this writer's business, not the caller's -
/// exactly as <see cref="ISaleWriter.InsertSaleAsync"/> keeps <c>sale</c>'s chain to itself.
/// </para>
/// <para>
/// <see cref="IncrementQtyReturnedAsync"/> is the one permitted update on <c>sale_line</c>
/// (docs/01_DATA_MODEL.md §6, <c>trg_sale_line_qty_returned_bounds</c>). It adds to whatever is
/// already there rather than setting an absolute figure, because the database bound is monotonic
/// and cumulative (AC-06) - the caller has already checked the increment is safe
/// (<c>IReturnPolicyAuthorisationService.AuthoriseCumulativeQuantity</c>) before this is ever
/// called; the trigger is the backstop that makes it true even if a future caller forgets to.
/// </para>
/// </remarks>
public interface ISaleReturnWriter
{
    /// <summary>Inserts the return header and returns its id.</summary>
    public Task<long> InsertSaleReturnAsync(NewSaleReturn saleReturn, CancellationToken cancellationToken = default);

    /// <summary>Inserts one return line.</summary>
    public Task InsertSaleReturnLineAsync(
        long saleReturnId, NewSaleReturnLine line, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="quantityBase"/> to <c>sale_line.qty_returned</c> for
    /// <paramref name="saleLineId"/> - never sets it outright.
    /// </summary>
    public Task IncrementQtyReturnedAsync(
        long saleLineId, Quantity quantityBase, CancellationToken cancellationToken = default);

    /// <summary>Inserts one refund payment row against the return, with <c>sale_id</c> left null.</summary>
    public Task InsertRefundPaymentAsync(
        long saleReturnId, NewTender tender, CancellationToken cancellationToken = default);
}
