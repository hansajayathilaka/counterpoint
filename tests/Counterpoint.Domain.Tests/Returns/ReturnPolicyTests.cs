using System;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Returns;

/// <summary>
/// The pure return-eligibility rules <c>Counterpoint.Application.Returns.ReturnPolicyAuthorisationService</c>
/// enforces (SRS FR-5, BR-*, FR-10.5, Q-03, task P2-T01 step 3). One test per rule, named for the
/// requirement it proves.
/// </summary>
public sealed class ReturnPolicyTests
{
    private const long MetreUom = 1;

    [Fact]
    public void FR_5_6_AReturnWithinTheWindowIsAllowedOutright()
    {
        var saleDate = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var now = saleDate.AddDays(10);

        var eligibility = ReturnPolicy.EvaluateReturnWindow(saleDate, now, windowDays: 14);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void FR_5_6_AReturnExactlyOnTheWindowsLastDayIsStillAllowed()
    {
        var saleDate = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var now = saleDate.AddDays(14);

        var eligibility = ReturnPolicy.EvaluateReturnWindow(saleDate, now, windowDays: 14);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>("the boundary itself is still inside the window");
    }

    [Fact]
    public void FR_5_6_AReturnOutsideTheReturnWindowIsAllowedOnlyWithAnOwnerOverride()
    {
        var saleDate = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var now = saleDate.AddDays(15);

        var eligibility = ReturnPolicy.EvaluateReturnWindow(saleDate, now, windowDays: 14);

        var allowedWithOverride = eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>().Subject;
        allowedWithOverride.Reason.Should().Contain("14-day");
    }

    [Fact]
    public void FR_5_10_AProductFlaggedNonReturnableIsAllowedOnlyWithAnOwnerOverride()
    {
        var eligibility = ReturnPolicy.EvaluateNonReturnable(productNonReturnable: true, categoryNonReturnable: false);

        eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
    }

    [Fact]
    public void AC_05_ACutGoodsItemFlaggedNonReturnableIsBlockedAndOnlyProceedsWithAnOwnerOverride()
    {
        // "Cut goods & mixed paint are non-returnable" - Q-03's answer, enforced through the
        // per-product flag P1-T05 already carries (AC-05).
        var eligibility = ReturnPolicy.EvaluateNonReturnable(productNonReturnable: true, categoryNonReturnable: false);

        var allowedWithOverride = eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>().Subject;
        allowedWithOverride.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FR_5_10_AProductInANonReturnableCategoryIsAllowedOnlyWithAnOwnerOverride()
    {
        var eligibility = ReturnPolicy.EvaluateNonReturnable(productNonReturnable: false, categoryNonReturnable: true);

        eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
    }

    [Fact]
    public void FR_5_10_AnOrdinaryProductInAnOrdinaryCategoryIsAllowedOutright()
    {
        var eligibility = ReturnPolicy.EvaluateNonReturnable(productNonReturnable: false, categoryNonReturnable: false);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void BR_05_APartialReturnWithinWhatRemainsIsAllowed()
    {
        var sold = Quantity.FromDecimal(10m, MetreUom);
        var alreadyReturned = Quantity.FromDecimal(4m, MetreUom);
        var requested = Quantity.FromDecimal(6m, MetreUom);

        var eligibility = ReturnPolicy.EvaluateCumulativeQuantity(sold, alreadyReturned, requested);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>("4 + 6 == 10 exactly consumes what was sold");
    }

    [Fact]
    public void AC_06_ACumulativeOverReturnAgainstABillLineIsDeniedWithNoOverridePath()
    {
        var sold = Quantity.FromDecimal(10m, MetreUom);
        var alreadyReturned = Quantity.FromDecimal(8m, MetreUom);
        var requested = Quantity.FromDecimal(3m, MetreUom);

        var eligibility = ReturnPolicy.EvaluateCumulativeQuantity(sold, alreadyReturned, requested);

        // Denied, never AllowedWithOverride - there is no third arm to fall into and no
        // override-consuming overload of this method exists anywhere (see
        // Counterpoint.Application.Returns.IReturnPolicyAuthorisationService.AuthoriseCumulativeQuantity).
        eligibility.Should().BeOfType<ReturnEligibility.Denied>();
    }

    [Fact]
    public void FR_5_ReturningALineAlreadyFullyReturnedIsDenied()
    {
        var sold = Quantity.FromDecimal(10m, MetreUom);
        var alreadyReturned = Quantity.FromDecimal(10m, MetreUom);
        var requested = Quantity.FromDecimal(1m, MetreUom);

        var eligibility = ReturnPolicy.EvaluateCumulativeQuantity(sold, alreadyReturned, requested);

        var denied = eligibility.Should().BeOfType<ReturnEligibility.Denied>().Subject;
        denied.Reason.Should().Contain("already been returned in full");
    }

    [Fact]
    public void AC_06_ARequestForExactlyWhatRemainsIsAllowedNotDenied()
    {
        var sold = Quantity.FromDecimal(10m, MetreUom);
        var alreadyReturned = Quantity.FromDecimal(7m, MetreUom);
        var requested = Quantity.FromDecimal(3m, MetreUom);

        var eligibility = ReturnPolicy.EvaluateCumulativeQuantity(sold, alreadyReturned, requested);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>("the boundary itself is still within what was sold");
    }

    [Fact]
    public void ARequestedQuantityOfZeroOrLessIsRefused()
    {
        var sold = Quantity.FromDecimal(10m, MetreUom);
        var alreadyReturned = Quantity.Zero(MetreUom);

        var zero = () => ReturnPolicy.EvaluateCumulativeQuantity(sold, alreadyReturned, Quantity.Zero(MetreUom));
        var negative = () => ReturnPolicy.EvaluateCumulativeQuantity(
            sold, alreadyReturned, Quantity.FromDecimal(-1m, MetreUom));

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FR_5_19_AnUnlinkedReturnWhenUnlinkedReturnsAreDisabledIsDeniedWithNoOverridePath()
    {
        var eligibility = ReturnPolicy.EvaluateUnlinkedReturn(isUnlinked: true, unlinkedReturnsAllowed: false);

        eligibility.Should().BeOfType<ReturnEligibility.Denied>(
            "the service refuses outright while the feature is off - the way round it is to enable "
            + "the setting, not to override the individual transaction");
    }

    [Fact]
    public void FR_5_19_AnUnlinkedReturnWhenEnabledStillRequiresAnOwnerOverrideEveryTime()
    {
        var eligibility = ReturnPolicy.EvaluateUnlinkedReturn(isUnlinked: true, unlinkedReturnsAllowed: true);

        eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>(
            "task P2-T03 step 2: enabling the setting does not skip the per-transaction override");
    }

    [Fact]
    public void FR_5_1_ALinkedReturnIsNotSubjectToTheUnlinkedReturnRuleAtAll()
    {
        var eligibility = ReturnPolicy.EvaluateUnlinkedReturn(isUnlinked: false, unlinkedReturnsAllowed: false);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void Q_03_AReturnWithoutABillNumberWhenAReceiptIsRequiredIsAllowedOnlyWithAnOwnerOverride()
    {
        var eligibility = ReturnPolicy.EvaluateReceiptRequirement(receiptRequired: true, billReferencePresented: false);

        eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
    }

    [Fact]
    public void Q_03_AReturnWithTheBillNumberPresentedNeedsNoOverrideEvenWhenAReceiptIsRequired()
    {
        var eligibility = ReturnPolicy.EvaluateReceiptRequirement(receiptRequired: true, billReferencePresented: true);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void FR_5_1_TheReceiptRequirementDoesNotApplyAtAllWhenTheShopHasSwitchedItOff()
    {
        var eligibility = ReturnPolicy.EvaluateReceiptRequirement(receiptRequired: false, billReferencePresented: false);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void FR_5_13_ACashRefundAboveTheConfiguredLimitIsAllowedOnlyWithAnOwnerOverride()
    {
        var eligibility = ReturnPolicy.EvaluateCashRefundLimit(
            isCashRefund: true, Money.FromDecimal(6000m), cashRefundLimit: Money.FromDecimal(5000m));

        var allowedWithOverride = eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>().Subject;
        allowedWithOverride.Reason.Should().Contain("5000");
    }

    [Fact]
    public void FR_5_13_ACashRefundAtExactlyTheLimitDoesNotExceedIt()
    {
        var eligibility = ReturnPolicy.EvaluateCashRefundLimit(
            isCashRefund: true, Money.FromDecimal(5000m), cashRefundLimit: Money.FromDecimal(5000m));

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void FR_5_13_AZeroCashRefundLimitMeansNoLimitAtAll()
    {
        var eligibility = ReturnPolicy.EvaluateCashRefundLimit(
            isCashRefund: true, Money.FromDecimal(1_000_000m), cashRefundLimit: Money.Zero);

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>("zero means no limit, the same convention PolicySettings.CashRefundLimit documents");
    }

    [Fact]
    public void FR_5_13_TheCashRefundLimitDoesNotApplyToARefundByAnyOtherMethod()
    {
        var eligibility = ReturnPolicy.EvaluateCashRefundLimit(
            isCashRefund: false, Money.FromDecimal(1_000_000m), cashRefundLimit: Money.FromDecimal(5000m));

        eligibility.Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public void ANegativeRefundAmountIsRefused()
    {
        var attempt = () => ReturnPolicy.EvaluateCashRefundLimit(
            isCashRefund: true, Money.FromDecimal(-1m), cashRefundLimit: Money.FromDecimal(5000m));

        attempt.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANegativeReturnWindowIsRefused()
    {
        var attempt = () => ReturnPolicy.EvaluateReturnWindow(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, windowDays: -1);

        attempt.Should().Throw<ArgumentOutOfRangeException>();
    }
}
