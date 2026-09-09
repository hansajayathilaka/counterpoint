using System;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// <see cref="IDiscountAuthorisationService"/>, reading the shop's caps from
/// <see cref="ISettings"/> on every call - the same "settings are read late, not captured at
/// start-up" reasoning as <c>SettingsRoundingPolicy</c>, so a changed policy limit governs the
/// very next discount without a restart (SRS FR-10.2's rationale, applied here to FR-10.5).
/// </summary>
public sealed class DiscountAuthorisationService : IDiscountAuthorisationService
{
    private readonly ISettings _settings;
    private readonly TimeProvider _timeProvider;

    public DiscountAuthorisationService(ISettings settings, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _settings = settings;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public DiscountEvaluation AuthoriseLineDiscount(
        DiscountInput discount,
        Money lineBaseAmount,
        Percentage? productMaxDiscountRate,
        OverrideToken? ownerOverride = null)
    {
        var evaluation = DiscountCapPolicy.Evaluate(
            discount, lineBaseAmount, productMaxDiscountRate, _settings.Policy.MaxLineDiscountRate);

        return Authorise(evaluation, PricingAuditActions.LineDiscountAboveLimit, ownerOverride);
    }

    /// <inheritdoc />
    public DiscountEvaluation AuthoriseBillDiscount(
        DiscountInput discount,
        Money billBaseAmount,
        OverrideToken? ownerOverride = null)
    {
        var evaluation = DiscountCapPolicy.Evaluate(
            discount, billBaseAmount, productMaxDiscountRate: null, _settings.Policy.MaxBillDiscountRate);

        return Authorise(evaluation, PricingAuditActions.BillDiscountAboveLimit, ownerOverride);
    }

    /// <summary>
    /// Within the cap, every discount is the cashier's own to give - nothing more to check. Above
    /// it, only a token granted for exactly this action, still valid and not already spent,
    /// lets it through (<see cref="OverrideToken.TryConsume"/>). The grant itself was already
    /// audited by <c>IOwnerOverrideService.RequestAsync</c>, naming both the cashier who asked
    /// and the owner who allowed it - there is nothing further to record here.
    /// </summary>
    private DiscountEvaluation Authorise(DiscountEvaluation evaluation, string action, OverrideToken? ownerOverride)
    {
        if (!evaluation.ExceedsCap)
        {
            return evaluation;
        }

        var now = _timeProvider.GetLocalNow();

        if (ownerOverride is null || !ownerOverride.TryConsume(action, now))
        {
            throw new DiscountLimitExceededException(evaluation, action);
        }

        return evaluation;
    }
}
