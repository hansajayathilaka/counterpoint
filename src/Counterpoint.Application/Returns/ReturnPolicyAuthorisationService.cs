using System;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// <see cref="IReturnPolicyAuthorisationService"/>, reading the shop's return policy from
/// <see cref="ISettings"/> on every call - the same "settings are read late, not captured at
/// start-up" reasoning as <c>SettingsRoundingPolicy</c> and
/// <c>Counterpoint.Application.Pricing.DiscountAuthorisationService</c>, so a changed policy
/// governs the very next return without a restart (SRS FR-10.2's rationale, applied here to
/// FR-10.5).
/// </summary>
public sealed class ReturnPolicyAuthorisationService : IReturnPolicyAuthorisationService
{
    private readonly ISettings _settings;
    private readonly TimeProvider _timeProvider;

    public ReturnPolicyAuthorisationService(ISettings settings, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _settings = settings;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public ReturnEligibility AuthoriseReturnWindow(DateTimeOffset saleDate, OverrideToken? ownerOverride = null)
    {
        var eligibility = ReturnPolicy.EvaluateReturnWindow(
            saleDate, _timeProvider.GetLocalNow(), _settings.Policy.ReturnWindowDays);

        return Authorise(eligibility, ReturnPolicyAuditActions.ReturnWindowExceeded, ownerOverride);
    }

    /// <inheritdoc />
    public ReturnEligibility AuthoriseNonReturnable(
        bool productNonReturnable, long? categoryId, OverrideToken? ownerOverride = null)
    {
        var categoryNonReturnable = categoryId is { } id && IsNonReturnableCategory(id);

        var eligibility = ReturnPolicy.EvaluateNonReturnable(productNonReturnable, categoryNonReturnable);

        return Authorise(eligibility, ReturnPolicyAuditActions.NonReturnableOverride, ownerOverride);
    }

    /// <inheritdoc />
    public ReturnEligibility AuthoriseCumulativeQuantity(
        Quantity soldQuantity, Quantity alreadyReturnedQuantity, Quantity requestedQuantity)
    {
        var eligibility = ReturnPolicy.EvaluateCumulativeQuantity(
            soldQuantity, alreadyReturnedQuantity, requestedQuantity);

        // No action, no OverrideToken parameter on this method at all - AC-06 is never
        // overridable, and this call site is the reason the interface offers no way to try.
        return eligibility is ReturnEligibility.Denied denied
            ? throw new ReturnNotEligibleException(denied, action: null)
            : eligibility;
    }

    /// <inheritdoc />
    public ReturnEligibility AuthoriseUnlinkedReturn(OverrideToken? ownerOverride = null)
    {
        var eligibility = ReturnPolicy.EvaluateUnlinkedReturn(
            isUnlinked: true, _settings.Policy.AllowUnlinkedReturns);

        return Authorise(eligibility, ReturnPolicyAuditActions.UnlinkedReturn, ownerOverride);
    }

    /// <inheritdoc />
    public ReturnEligibility AuthoriseReceiptRequirement(
        bool billReferencePresented, OverrideToken? ownerOverride = null)
    {
        var eligibility = ReturnPolicy.EvaluateReceiptRequirement(
            _settings.Policy.ReceiptRequired, billReferencePresented);

        return Authorise(eligibility, ReturnPolicyAuditActions.ReceiptNotPresented, ownerOverride);
    }

    /// <inheritdoc />
    public ReturnEligibility AuthoriseCashRefundLimit(
        bool isCashRefund, Money refundAmount, OverrideToken? ownerOverride = null)
    {
        var eligibility = ReturnPolicy.EvaluateCashRefundLimit(
            isCashRefund, refundAmount, _settings.Policy.CashRefundLimit);

        return Authorise(eligibility, ReturnPolicyAuditActions.CashRefundLimitExceeded, ownerOverride);
    }

    private bool IsNonReturnableCategory(long categoryId)
    {
        foreach (var id in _settings.Policy.NonReturnableCategoryIds)
        {
            if (id == categoryId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Within policy, a return is the cashier's own to take - nothing more to check. Outside it,
    /// only a token granted for exactly this action, still valid and not already spent, lets it
    /// through (<see cref="OverrideToken.TryConsume"/>). The grant itself was already audited by
    /// <see cref="IOwnerOverrideService.RequestAsync"/>, naming both the cashier who asked and the
    /// owner who allowed it - there is nothing further to record here.
    /// </summary>
    private ReturnEligibility Authorise(ReturnEligibility eligibility, string action, OverrideToken? ownerOverride)
    {
        if (eligibility is ReturnEligibility.Allowed)
        {
            return eligibility;
        }

        if (eligibility is ReturnEligibility.Denied denied)
        {
            // Denied never carries an override path - see the remarks on ReturnEligibility. A
            // token, if one was somehow passed in, is never even inspected here.
            throw new ReturnNotEligibleException(denied, action: null);
        }

        var now = _timeProvider.GetLocalNow();

        if (ownerOverride is null || !ownerOverride.TryConsume(action, now))
        {
            throw new ReturnNotEligibleException(eligibility, action);
        }

        return eligibility;
    }
}
