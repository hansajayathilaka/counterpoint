using System;
using System.Threading.Tasks;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Returns;

/// <summary>
/// The return policy engine reading real settings, granting and consuming real
/// <see cref="OverrideToken"/>s (SRS FR-5, BR-*, FR-10.5, Q-03, AC-05, AC-06, task P2-T01).
/// </summary>
public sealed class ReturnPolicyAuthorisationServiceTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";
    private const string Cashier = "priya";
    private const string CashierPassword = "counter1";
    private const long MetreUom = 1;

    [Fact]
    public async Task AC_05_ANonReturnableItemIsDeniedAndProceedsOnlyWithAnOwnerOverrideWhichIsAudited()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        var attempt = () => Task.FromResult(policy.AuthoriseNonReturnable(
            productNonReturnable: true, categoryId: null));

        var exception = await attempt.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
        exception.Which.Action.Should().Be(ReturnPolicyAuditActions.NonReturnableOverride);

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.NonReturnableOverride, "Customer insists, cut cable.", Owner, OwnerPassword));

        var eligibility = policy.AuthoriseNonReturnable(productNonReturnable: true, categoryId: null, token);
        eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();

        // FR-1.6: the grant is what is audited, naming both the cashier who asked and the owner
        // who allowed it.
        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}' "
            + $"AND user_id = {token.GrantedByUserId};"))
            .Should().Be(1);

        var payload = await fixture.ScalarAsync(
            $"SELECT after_json FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}';");
        payload.Should().Contain(ReturnPolicyAuditActions.NonReturnableOverride);

        // Single-use: a second non-returnable line cannot ride on the same authorisation.
        var reuse = () => Task.FromResult(policy.AuthoriseNonReturnable(
            productNonReturnable: true, categoryId: null, token));
        await reuse.Should().ThrowAsync<ReturnNotEligibleException>("the token was already spent by the first call");
    }

    [Fact]
    public async Task AC_06_ThereIsNoOverrideParameterThatCanLetACumulativeOverReturnThrough()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        var sold = Quantity.FromDecimal(10m, MetreUom);
        var alreadyReturned = Quantity.FromDecimal(8m, MetreUom);
        var requested = Quantity.FromDecimal(3m, MetreUom);

        // The method itself has no OverrideToken parameter to pass one to - see
        // IReturnPolicyAuthorisationService.AuthoriseCumulativeQuantity's own remarks.
        var attempt = () => Task.FromResult(policy.AuthoriseCumulativeQuantity(sold, alreadyReturned, requested));

        var exception = await attempt.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.Denied>();
        exception.Which.Action.Should().BeNull("no override action exists for this rule at all");
    }

    [Fact]
    public async Task FR_5_6_ChangingTheReturnWindowInSettingsChangesEnforcementImmediately()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();
        var clock = fixture.Resolve<TimeProvider>();

        var saleDate = clock.GetLocalNow().AddDays(-20);

        // Default window is 14 days (Q-03): 20 days ago is outside it.
        var attempt = () => Task.FromResult(policy.AuthoriseReturnWindow(saleDate));
        await attempt.Should().ThrowAsync<ReturnNotEligibleException>();

        await AsOwnerAsync(fixture, async () =>
        {
            await fixture.Resolve<ISettings>().UpdateAsync(
                s => s with { Policy = s.Policy with { ReturnWindowDays = 30 } });
        });

        // No restart, no rebuild - the very next call reads the new setting (SRS FR-10.2's
        // rationale, applied here to FR-10.5).
        policy.AuthoriseReturnWindow(saleDate).Should().BeOfType<ReturnEligibility.Allowed>();
    }

    [Fact]
    public async Task FR_5_19_UnlinkedReturnsDisabledInSettingsRefusesTheServiceMethodOutright()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        // AllowUnlinkedReturns is false by default (Q-03).
        var attempt = () => Task.FromResult(policy.AuthoriseUnlinkedReturn());

        var exception = await attempt.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.Denied>();
    }

    [Fact]
    public async Task FR_5_19_EnablingUnlinkedReturnsStillRequiresAnOwnerOverrideEveryTime()
    {
        await using var fixture = await SignedInAsCashierAsync();

        await AsOwnerAsync(fixture, async () =>
        {
            await fixture.Resolve<ISettings>().UpdateAsync(
                s => s with { Policy = s.Policy with { AllowUnlinkedReturns = true } });
        });

        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        var attempt = () => Task.FromResult(policy.AuthoriseUnlinkedReturn());
        await attempt.Should().ThrowAsync<ReturnNotEligibleException>(
            "task P2-T03 step 2: even enabled, an unlinked return still needs an override every time");

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.UnlinkedReturn, "Regular customer, no receipt kept.", Owner, OwnerPassword));

        policy.AuthoriseUnlinkedReturn(token).Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
    }

    [Fact]
    public async Task FR_5_13_ACashRefundAboveTheConfiguredLimitRequiresAnOwnerOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();

        await AsOwnerAsync(fixture, async () =>
        {
            await fixture.Resolve<ISettings>().UpdateAsync(
                s => s with { Policy = s.Policy with { CashRefundLimit = Money.FromDecimal(5000m) } });
        });

        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        var attempt = () => Task.FromResult(policy.AuthoriseCashRefundLimit(
            isCashRefund: true, Money.FromDecimal(6000m)));
        await attempt.Should().ThrowAsync<ReturnNotEligibleException>();

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.CashRefundLimitExceeded, "Owner approved, valued customer.", Owner, OwnerPassword));

        policy.AuthoriseCashRefundLimit(isCashRefund: true, Money.FromDecimal(6000m), token)
            .Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
    }

    [Fact]
    public async Task Q_03_AReturnWithoutABillNumberRequiresAnOwnerOverrideWhenAReceiptIsRequired()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        // ReceiptRequired is true by default (Q-03: "should have to previous bill no").
        var attempt = () => Task.FromResult(policy.AuthoriseReceiptRequirement(billReferencePresented: false));
        await attempt.Should().ThrowAsync<ReturnNotEligibleException>();

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.ReceiptNotPresented, "Bill lost, verified by phone number.", Owner, OwnerPassword));

        policy.AuthoriseReceiptRequirement(billReferencePresented: false, token)
            .Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
    }

    [Fact]
    public async Task AnOverrideGrantedForTheWrongActionDoesNotAuthoriseAReturn()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var policy = fixture.Resolve<IReturnPolicyAuthorisationService>();

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.CashRefundLimitExceeded, "Wrong action on purpose.", Owner, OwnerPassword));

        var attempt = () => Task.FromResult(policy.AuthoriseNonReturnable(
            productNonReturnable: true, categoryId: null, token));

        await attempt.Should().ThrowAsync<ReturnNotEligibleException>();
    }

    private static async Task AsOwnerAsync(SaleFixture fixture, Func<Task> action)
    {
        var authentication = fixture.Resolve<IAuthenticationService>();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(Owner, OwnerPassword)).Succeeded.Should().BeTrue();

        await action();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(Cashier, CashierPassword)).Succeeded.Should().BeTrue();
    }

    /// <summary>An owner account with a password, a cashier account, and the cashier signed in - same shape as DiscountAuthorisationServiceTests.</summary>
    private static async Task<SaleFixture> SignedInAsCashierAsync()
    {
        var fixture = await SaleFixture.CreateAsync();

        try
        {
            await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);

            var authentication = fixture.Resolve<IAuthenticationService>();
            await authentication.LogInAsync(Owner, OwnerPassword);

            await fixture.Resolve<IUserAdministration>().CreateAsync(
                new CreateUserCommand(Cashier, "Priya", CashierPassword, Role.Cashier));

            await authentication.LogOutAsync();
            (await authentication.LogInAsync(Cashier, CashierPassword)).Succeeded.Should().BeTrue();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }
}
