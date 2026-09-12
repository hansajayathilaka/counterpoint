using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Finds the original bill a linked return is taken against (SRS FR-5, task P2-T02 step 1).
/// </summary>
/// <remarks>
/// A read connection, not the write one, and read before the return's own transaction opens -
/// the same NFR-P3 discipline <see cref="ISaleLookup"/> keeps for cancellation.
/// <c>Counterpoint.Application.Returns.CreateReturnHandler</c> re-reads by
/// <see cref="FindBySaleIdAsync"/> at the moment it actually builds the return, so a screen that
/// found the bill by <see cref="FindByBillNoAsync"/> or <see cref="SearchAsync"/> a minute
/// earlier can never hand the handler a stale quantity-returned figure - <c>sale</c> and
/// <c>sale_line</c> are read fresh either way.
/// </remarks>
public interface IReturnableSaleLookup
{
    /// <summary>
    /// The bill named by its own number - typed, or scanned off the barcode a receipt already
    /// carries (SRS FR-5.1). Null when no such bill exists.
    /// </summary>
    public Task<ReturnableSale?> FindByBillNoAsync(string billNo, CancellationToken cancellationToken = default);

    /// <summary>The bill by its id, for a caller that already has one - the return command's own re-read.</summary>
    public Task<ReturnableSale?> FindBySaleIdAsync(long saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bills matching a date range, a customer name fragment or an approximate amount (SRS
    /// FR-5.1's "or search by date/customer/amount"), most recent first.
    /// </summary>
    public Task<IReadOnlyList<ReturnSaleSearchResult>> SearchAsync(
        ReturnSaleSearchCriteria criteria, CancellationToken cancellationToken = default);
}
