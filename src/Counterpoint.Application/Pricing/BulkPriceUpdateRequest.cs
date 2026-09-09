using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Pricing;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// What <see cref="IBulkPriceUpdateService"/> needs to preview or apply a bulk price change (SRS
/// FR-2.19).
/// </summary>
/// <param name="Filter">Which variants to touch - by category, brand or supplier.</param>
/// <param name="Adjustment">By how much, and in which direction.</param>
/// <param name="ConfirmBelowCost">
/// True to apply the update even though it would put one or more resulting prices at or below
/// their product's cost (SRS FR-2.18). False - the default - is what a first attempt should
/// send; <see cref="IBulkPriceUpdateService.PreviewAsync"/> flags every line this would affect
/// before the shop decides.
/// </param>
/// <param name="Reason">Why the update is happening, for every <c>price_change_log</c> row it writes (SRS FR-2.17).</param>
public sealed record BulkPriceUpdateRequest(
    BulkPriceQueryFilter Filter,
    PriceAdjustment Adjustment,
    bool ConfirmBelowCost = false,
    string? Reason = null);
