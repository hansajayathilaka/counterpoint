using System;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The owner override a cashier calls for at the counter (SRS FR-1.7, FR-1.6, NFR-S9).
/// </summary>
/// <remarks>
/// The commands that spend a token - unlinked return, over-limit discount, no-sale drawer - are
/// P2-T03, P1-T08 and P3-T01. What is proved here is the mechanism those will use.
/// </remarks>
public sealed class OwnerOverrideTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";
    private const string Cashier = "priya";
    private const string CashierPassword = "counter1";
    private const string Action = "DISCOUNT_ABOVE_LIMIT";

    [Fact]
    public async Task FR_1_7_AnOwnerAuthorisesTheCashierWithoutSigningThemOut()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashierId = fixture.Resolve<ISession>().CurrentUser!.Id;

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "Regular trade customer.", Owner, OwnerPassword));

        token.Action.Should().Be(Action);
        token.RequestedByUserId.Should().Be(cashierId);
        token.GrantedByUserId.Should().NotBe(cashierId);

        // FR-1.7 in full: the cashier is still the one on the till, and the bill on the screen
        // was never touched.
        var session = fixture.Resolve<ISession>();
        session.CurrentUser!.Username.Should().Be(Cashier);
        session.Role.Should().Be(Role.Cashier);
    }

    [Fact]
    public async Task FR_1_6_TheAuditRowNamesBothUsersAndTheReason()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var cashierId = fixture.Resolve<ISession>().CurrentUser!.Id;

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "Regular trade customer.", Owner, OwnerPassword));

        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}' AND user_id = {token.GrantedByUserId};"))
            .Should().Be(1);

        var payload = await fixture.ScalarAsync(
            $"SELECT after_json FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}';");

        payload.Should().Contain($"\"requested_by_user_id\":{cashierId}");
        payload.Should().Contain($"\"granted_by_user_id\":{token.GrantedByUserId}");
        payload.Should().Contain(Action);

        (await fixture.ScalarAsync(
            $"SELECT reason FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideGranted}';"))
            .Should().Be("Regular trade customer.");
    }

    [Fact]
    public async Task FR_1_7_TheTokenIsSpentOnceByTheCallingCommand()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "Regular trade customer.", Owner, OwnerPassword));

        var now = fixture.Resolve<TimeProvider>().GetLocalNow();

        token.TryConsume(Action, now).Should().BeTrue();
        token.TryConsume(Action, now).Should().BeFalse();
    }

    [Fact]
    public async Task FR_1_7_TheWrongOwnerPasswordIsRefusedAndRecorded()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var request = () => fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "Trying it on.", Owner, "wrong"));

        await request.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideRefused}';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task NFR_S9_AnOverridePromptIsNotAWayRoundTheLockout()
    {
        await using var fixture = await SignedInAsCashierAsync();
        var overrides = fixture.Resolve<IOwnerOverrideService>();

        for (var attempt = 0; attempt < AccountLockout.MaxFailedAttempts; attempt++)
        {
            var guess = () => overrides.RequestAsync(
                new OwnerOverrideRequest(Action, "Trying it on.", Owner, "wrong"));

            await guess.Should().ThrowAsync<NotAuthorisedException>();
        }

        (await fixture.ScalarAsync("SELECT locked_until FROM app_user WHERE username = 'owner';"))
            .Should().NotBeNull("the same rate limit applies whichever box the password is typed into");

        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.ReauthenticationFailed}';"))
            .Should().Be(5);
    }

    [Fact]
    public async Task FR_1_7_ACashiersOwnCredentialCannotAuthoriseAnOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var request = () => fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "Authorising myself.", Cashier, CashierPassword));

        await request.Should().ThrowAsync<NotAuthorisedException>().WithMessage("*not an owner*");

        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerOverrideRefused}';"))
            .Should().Be(1, "an attempt to self-authorise is worth seeing");
    }

    [Fact]
    public async Task FR_1_6_AnOverrideWithNoReasonIsRefused()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var request = () => fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "   ", Owner, OwnerPassword));

        await request.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reason*");
    }

    [Fact]
    public async Task FR_1_7_NobodySignedInMeansThereIsNobodyToAuthorise()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);

        var request = () => fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(Action, "Nobody is here.", Owner, OwnerPassword));

        await request.Should().ThrowAsync<NotAuthorisedException>().WithMessage("*Sign in first*");
    }

    /// <summary>An owner account with a password, a cashier account, and the cashier signed in.</summary>
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
