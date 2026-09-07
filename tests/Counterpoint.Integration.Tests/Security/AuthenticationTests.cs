using System;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// Sign-in, failure counting, lockout and the audit trail, over a real encrypted database
/// (SRS FR-1.1, FR-1.6, FR-1.8, NFR-S1, NFR-S9).
/// </summary>
/// <remarks>
/// A real SQLite file through the production wiring, not the in-memory provider: the point is
/// that <c>app_user</c> really is updated, that <c>audit_log</c> really does accept the rows, and
/// that the append-only triggers and the hash chain on it do not object.
/// </remarks>
public sealed class AuthenticationTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";

    [Fact]
    public async Task FR_1_1_TheRightPasswordSignsInAndStampsLastLogin()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var result = await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, OwnerPassword);

        result.Succeeded.Should().BeTrue();
        result.Outcome.Should().Be(LoginOutcome.Succeeded);
        result.User!.Username.Should().Be(Owner);
        result.User.Role.Should().Be(Role.Owner);

        var session = fixture.Resolve<ISession>();
        session.IsAuthenticated.Should().BeTrue();
        session.CurrentUser!.DisplayName.Should().Be("Shop Owner");
        session.Role.Should().Be(Role.Owner);

        (await fixture.ScalarAsync("SELECT last_login FROM app_user WHERE username = 'owner';"))
            .Should().NotBeNull("a successful sign-in stamps last_login");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'LOGIN_SUCCEEDED';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task FR_1_1_TheSessionPicksUpTheOpenShift()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, OwnerPassword);

        // C-01 permits exactly one open shift; the seeder opens it. Opening and closing one is
        // P1-T14 and P3-T01 - this only reflects what is already open.
        var shiftId = await fixture.ScalarAsync("SELECT id FROM shift WHERE status = 'OPEN';");

        fixture.Resolve<ISession>().ShiftId
            .Should().Be(long.Parse(shiftId!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task FR_1_1_AWrongPasswordIsRefusedAndTheSessionStaysEmpty()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var result = await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, "wrong");

        result.Succeeded.Should().BeFalse();
        result.Outcome.Should().Be(LoginOutcome.InvalidCredentials);
        result.User.Should().BeNull();
        fixture.Resolve<ISession>().IsAuthenticated.Should().BeFalse();

        (await fixture.ScalarAsync("SELECT failed_attempts FROM app_user WHERE username = 'owner';"))
            .Should().Be("1");
    }

    [Fact]
    public async Task NFR_S9_AnUnknownUsernameLooksExactlyLikeAWrongPasswordAndIsStillLogged()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var known = await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, "wrong");
        var unknown = await fixture.Resolve<IAuthenticationService>().LogInAsync("mallory", "wrong");

        unknown.Outcome.Should().Be(known.Outcome);
        unknown.Message.Should().Be(known.Message, "telling them apart enumerates the shop's usernames");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'LOGIN_FAILED' AND user_id IS NULL;"))
            .Should().Be(1, "something was attempted and no app_user row owns it - that is the evidence");
    }

    [Fact]
    public async Task NFR_S9_FiveFailuresLockTheAccountAndEveryAttemptIsLogged()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var authentication = fixture.Resolve<IAuthenticationService>();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var result = await authentication.LogInAsync(Owner, "wrong");

            result.Outcome.Should().Be(
                LoginOutcome.InvalidCredentials,
                "attempt {0} is short of the limit",
                attempt);
        }

        (await fixture.ScalarAsync("SELECT locked_until FROM app_user WHERE username = 'owner';"))
            .Should().BeNull("four failures is not five");

        var fifth = await authentication.LogInAsync(Owner, "wrong");

        fifth.Outcome.Should().Be(LoginOutcome.LockedOut);
        fifth.LockedUntil.Should().NotBeNull();

        (await fixture.ScalarAsync("SELECT failed_attempts FROM app_user WHERE username = 'owner';"))
            .Should().Be("5");
        (await fixture.ScalarAsync("SELECT locked_until FROM app_user WHERE username = 'owner';"))
            .Should().NotBeNull();

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'LOGIN_FAILED';"))
            .Should().Be(5, "NFR-S9 asks for every failed attempt to be logged, not just the last one");
    }

    [Fact]
    public async Task NFR_S9_TheRightPasswordDuringALockoutIsStillRefusedAndRecorded()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var authentication = fixture.Resolve<IAuthenticationService>();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await authentication.LogInAsync(Owner, "wrong");
        }

        var result = await authentication.LogInAsync(Owner, OwnerPassword);

        result.Outcome.Should().Be(LoginOutcome.LockedOut, "the lock is what a rate limit is");
        result.Message.Should().Contain("locked");
        fixture.Resolve<ISession>().IsAuthenticated.Should().BeFalse();

        (await fixture.ScalarAsync("SELECT failed_attempts FROM app_user WHERE username = 'owner';"))
            .Should().Be("5", "attempts made while locked do not extend the lock - see AccountLockout");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'LOGIN_REFUSED';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task NFR_S9_ACorrectPasswordClearsTheFailureCounter()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var authentication = fixture.Resolve<IAuthenticationService>();

        await authentication.LogInAsync(Owner, "wrong");
        await authentication.LogInAsync(Owner, "wrong");

        (await fixture.ScalarAsync("SELECT failed_attempts FROM app_user WHERE username = 'owner';"))
            .Should().Be("2");

        (await authentication.LogInAsync(Owner, OwnerPassword)).Succeeded.Should().BeTrue();

        (await fixture.ScalarAsync("SELECT failed_attempts FROM app_user WHERE username = 'owner';"))
            .Should().Be("0", "the counter is consecutive failures, not failures ever");
    }

    [Fact]
    public async Task FR_1_4_ADeactivatedAccountCannotSignInEvenWithTheRightPassword()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogInAsync(Owner, OwnerPassword);

        var users = fixture.Resolve<IUserAdministration>();
        var cashierId = await users.CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));
        await users.DeactivateAsync(cashierId);

        await authentication.LogOutAsync();

        var result = await authentication.LogInAsync("priya", "counter1");

        result.Outcome.Should().Be(LoginOutcome.Deactivated);
        result.Message.Should().Contain("turned off");
        fixture.Resolve<ISession>().IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task FR_1_8_SigningOutEndsTheSessionAndSaysSoInTheAuditLog()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogInAsync(Owner, OwnerPassword);

        await authentication.LogOutAsync();

        fixture.Resolve<ISession>().IsAuthenticated.Should().BeFalse();
        fixture.Resolve<ISession>().ShiftId.Should().BeNull();

        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'LOGOUT';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task FR_1_3_NoPasswordIsStoredAnywhereInTheDatabase()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await GiveTheOwnerAPasswordAsync(fixture);

        await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, "wrong");
        await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, OwnerPassword);

        var hash = await fixture.ScalarAsync("SELECT password_hash FROM app_user WHERE username = 'owner';");

        hash.Should().StartWith("$argon2id$", "hashes are Argon2id, and nothing else is stored");
        hash.Should().NotContain(OwnerPassword);

        // Not in the audit trail either - and the audit trail is the one thing here that cannot
        // be deleted afterwards.
        (await fixture.CountAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT COUNT(*) FROM audit_log WHERE COALESCE(after_json,'') || COALESCE(before_json,'') || COALESCE(reason,'') LIKE '%{OwnerPassword}%';")))
            .Should().Be(0);
    }

    /// <summary>
    /// Gets past the deliberate chicken-and-egg: the seeded owner has no usable password, because
    /// there is no default password anywhere in this system (docs/01_DATA_MODEL.md §11).
    /// </summary>
    private static Task GiveTheOwnerAPasswordAsync(SaleFixture fixture) =>
        fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);
}
