using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// Bulk price update by category, brand or supplier, by percentage or fixed amount, with a
/// preview before applying (SRS FR-2.19). Owner only, the same as every other catalogue
/// maintenance screen (SRS §3.3 ROLE-2, NFR-S2, AC-17).
/// </summary>
[RequiresRole(Role.Owner)]
public interface IBulkPriceUpdateService
{
    /// <summary>
    /// Works out what <paramref name="request"/> would change, without writing anything - the
    /// "preview before applying" FR-2.19 requires.
    /// </summary>
    public Task<BulkPriceUpdatePreview> PreviewAsync(
        BulkPriceUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="request"/>: every matching variant's price is updated and logged
    /// to <c>price_change_log</c> (SRS FR-2.17), in one transaction.
    /// </summary>
    /// <returns>How many variants were changed.</returns>
    /// <exception cref="BulkPriceBelowCostWarningException">
    /// The update would put one or more prices at or below cost and
    /// <see cref="BulkPriceUpdateRequest.ConfirmBelowCost"/> is false (SRS FR-2.18).
    /// </exception>
    public Task<int> ApplyAsync(
        BulkPriceUpdateRequest request,
        CancellationToken cancellationToken = default);
}
