using System.Threading.Tasks;
using Counterpoint.Application.Pricing;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Pricing;

/// <summary>
/// Discounts above the cap require an owner override, and the override is audited (SRS FR-1.6,
/// FR-1.7, Q-12, task P1-T08 step 2, done-when: "A 12% line discount with a 5% cap requires an
/// owner override, and the override is audited").
/// </summary>
public sealed class DiscountAuthorisationServiceTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";
    private const string Cashier = "priya";
    private const string CashierPassword = "counter1";

    [Fact]
    public async Task Q_12_ATwelvePercentLineDiscountAgainstAFivePercentProductCapIsRefusedWithoutAnOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var discounts = fixture.Resolve<IDiscountAuthorisationService>();

        var discount = DiscountInput.OfRate(Percentage.FromPercent(12m));
        var lineBaseAmount = Money.FromDecimal(100m);

        var attempt = () => Task.FromResult(discounts.AuthoriseLineDiscount(
            discount, lineBaseAmount, Percentage.FromPercent(5m)));

        var exception = await attempt.Should().ThrowAsync<DiscountLimitExceededException>();
        exception.Which.Evaluation.Rate.Should().Be(Percentage.FromPercent(12m));
        exception.Which.Evaluation.Cap.Should().Be(Percentage.FromPercent(5m));
        exception.Which.Action.Should().Be(PricingAuditActions.LineDiscountAboveLimit);
    }

    [Fact]
    public async Task Q_12_ATwelvePercentLineDiscountAgainstAFivePercentProductCapSucceedsWithAConsumedOwnerOverrideAndTheOverrideIsAudited()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var discounts = fixture.Resolve<IDiscountAuthorisationService>();
        var overrides = fixture.Resolve<IOwnerOverrideService>();
        var cashierId = fixture.Resolve<ISession>().CurrentUser!.Id;

        var discount = DiscountInput.OfRate(Percentage.FromPercent(12m));
        var lineBaseAmount = Money.FromDecimal(100m);

        var token = await overrides.RequestAsync(new OwnerOverrideRequest(
            PricingAuditActions.LineDiscountAboveLimit, "Regular trade customer.", Owner, OwnerPassword));

        var evaluation = discounts.AuthoriseLineDiscount(discount, lineBaseAmount, Percentage.FromPercent(5m), token);

        evaluation.ExceedsCap.Should().BeTrue();
        evaluation.Amount.Should().Be(Money.FromDecimal(12m));

        // FR-1.6: the grant itself is what is audited, naming both the cashier who asked and the
        // owner who allowed it - AuthoriseLineDiscount spends the token, it does not audit again.
        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}' "
            + $"AND user_id = {token.GrantedByUserId};"))
            .Should().Be(1);

        var payload = await fixture.ScalarAsync(
            $"SELECT after_json FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}';");
        payload.Should().Contain(PricingAuditActions.LineDiscountAboveLimit);
        payload.Should().Contain($"\"requested_by_user_id\":{cashierId}");

        // The token is single-use: a second discount cannot ride on the same authorisation.
        var reuse = () => Task.FromResult(discounts.AuthoriseLineDiscount(
            discount, lineBaseAmount, Percentage.FromPercent(5m), token));
        await reuse.Should().ThrowAsync<DiscountLimitExceededException>("the token was already spent by the first call");
    }

    [Fact]
    public async Task Q_12_ADiscountWithinTheProductsCapNeedsNoOverrideAtAll()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var discounts = fixture.Resolve<IDiscountAuthorisationService>();

        var evaluation = discounts.AuthoriseLineDiscount(
            DiscountInput.OfRate(Percentage.FromPercent(3m)),
            Money.FromDecimal(100m),
            Percentage.FromPercent(5m));

        evaluation.ExceedsCap.Should().BeFalse();
        evaluation.Amount.Should().Be(Money.FromDecimal(3m));
    }

    [Fact]
    public async Task Q_12_TheProductsOwnCapAppliesEvenWhenThePolicyLimitIsWideOpen()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var discounts = fixture.Resolve<IDiscountAuthorisationService>();

        // The default shop-wide policy limit is 100% (SRS FR-10.5, "not for now" on Q-12), so
        // this discount is refused only because the product's own cap is tighter.
        var attempt = () => Task.FromResult(discounts.AuthoriseLineDiscount(
            DiscountInput.OfRate(Percentage.FromPercent(12m)),
            Money.FromDecimal(100m),
            Percentage.FromPercent(5m)));

        await attempt.Should().ThrowAsync<DiscountLimitExceededException>();
    }

    [Fact]
    public async Task Q_12_ABillDiscountAboveThePolicyLimitIsRefusedThenSucceedsWithAnOwnerOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();

        await fixture.Resolve<IAuthenticationService>().LogOutAsync();
        (await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, OwnerPassword)).Succeeded.Should().BeTrue();
        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { MaxBillDiscountRate = Percentage.FromPercent(10m) } });
        await fixture.Resolve<IAuthenticationService>().LogOutAsync();
        (await fixture.Resolve<IAuthenticationService>().LogInAsync(Cashier, CashierPassword)).Succeeded.Should().BeTrue();

        var discounts = fixture.Resolve<IDiscountAuthorisationService>();
        var bigDiscount = DiscountInput.OfRate(Percentage.FromPercent(15m));
        var billBaseAmount = Money.FromDecimal(200m);

        var attempt = () => Task.FromResult(discounts.AuthoriseBillDiscount(bigDiscount, billBaseAmount));
        var exception = await attempt.Should().ThrowAsync<DiscountLimitExceededException>();
        exception.Which.Action.Should().Be(PricingAuditActions.BillDiscountAboveLimit);

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            PricingAuditActions.BillDiscountAboveLimit, "Closing-down clearance.", Owner, OwnerPassword));

        var evaluation = discounts.AuthoriseBillDiscount(bigDiscount, billBaseAmount, token);
        evaluation.Amount.Should().Be(Money.FromDecimal(30m));
    }

    [Fact]
    public async Task AnOverrideGrantedForTheWrongActionDoesNotAuthoriseADiscount()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var discounts = fixture.Resolve<IDiscountAuthorisationService>();

        // A token granted for the bill-discount action cannot be spent on a line discount.
        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            PricingAuditActions.BillDiscountAboveLimit, "Wrong action on purpose.", Owner, OwnerPassword));

        var attempt = () => Task.FromResult(discounts.AuthoriseLineDiscount(
            DiscountInput.OfRate(Percentage.FromPercent(12m)),
            Money.FromDecimal(100m),
            Percentage.FromPercent(5m),
            token));

        await attempt.Should().ThrowAsync<DiscountLimitExceededException>();
    }

    /// <summary>An owner account with a password, a cashier account, and the cashier signed in - same shape as OwnerOverrideTests.</summary>
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
