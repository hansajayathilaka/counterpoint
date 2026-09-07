using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The owner's user management, over a real database (SRS FR-1.4, FR-1.6, AC-17).
/// </summary>
public sealed class UserAdministrationTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";

    [Fact]
    public async Task FR_1_4_AnOwnerCreatesACashierWhoCanThenSignIn()
    {
        await using var fixture = await SignedInAsOwnerAsync();

        var id = await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        id.Should().BeGreaterThan(0);

        (await fixture.ScalarAsync("SELECT role FROM app_user WHERE username = 'priya';"))
            .Should().Be("CASHIER");
        (await fixture.ScalarAsync("SELECT password_hash FROM app_user WHERE username = 'priya';"))
            .Should().StartWith("$argon2id$");

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();

        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task FR_1_6_EveryChangeIsRecordedAgainstTheOwnerWhoMadeIt()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();

        var id = await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));
        await users.DeactivateAsync(id);
        await users.ReactivateAsync(id);
        await users.ResetPasswordAsync(id, "counter2");

        var ownerId = fixture.Resolve<ISession>().CurrentUser!.Id;

        foreach (var action in new[]
                 {
                     SecurityAuditActions.UserCreated,
                     SecurityAuditActions.UserDeactivated,
                     SecurityAuditActions.UserReactivated,
                     SecurityAuditActions.UserPasswordReset,
                 })
        {
            (await fixture.CountAsync(
                $"SELECT COUNT(*) FROM audit_log WHERE action = '{action}' AND user_id = {ownerId} AND entity_id = {id};"))
                .Should().Be(1, "{0} is recorded against the owner who did it", action);
        }
    }

    [Fact]
    public async Task FR_1_4_AResetPasswordWorksAndClearsALockout()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();
        var authentication = fixture.Resolve<IAuthenticationService>();

        var id = await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));
        await authentication.LogOutAsync();

        for (var attempt = 0; attempt < AccountLockout.MaxFailedAttempts; attempt++)
        {
            await authentication.LogInAsync("priya", "wrong");
        }

        (await fixture.ScalarAsync("SELECT locked_until FROM app_user WHERE username = 'priya';"))
            .Should().NotBeNull();

        await authentication.LogInAsync(Owner, OwnerPassword);
        await users.ResetPasswordAsync(id, "counter2");
        await authentication.LogOutAsync();

        // The owner is standing right there; making the shop wait out the backoff as well would
        // be a punishment with no purpose left.
        (await authentication.LogInAsync("priya", "counter2")).Succeeded.Should().BeTrue();
        (await fixture.ScalarAsync("SELECT failed_attempts FROM app_user WHERE username = 'priya';"))
            .Should().Be("0");
    }

    [Fact]
    public async Task FR_1_4_TheLastEnabledOwnerAccountCannotBeTurnedOff()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();
        var ownerId = fixture.Resolve<ISession>().CurrentUser!.Id;

        var deactivate = () => users.DeactivateAsync(ownerId);

        await deactivate.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*only owner account*");

        (await fixture.ScalarAsync("SELECT active FROM app_user WHERE username = 'owner';"))
            .Should().Be("1");
    }

    [Fact]
    public async Task FR_1_4_AnOwnerCanBeTurnedOffOnceThereIsASecondOne()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();
        var ownerId = fixture.Resolve<ISession>().CurrentUser!.Id;

        await users.CreateAsync(new CreateUserCommand("manager", "Manager", "office99", Role.Owner));

        await users.DeactivateAsync(ownerId);

        (await fixture.ScalarAsync("SELECT active FROM app_user WHERE username = 'owner';"))
            .Should().Be("0");
    }

    [Fact]
    public async Task FR_1_4_ADuplicateUsernameIsRefusedInPlainLanguage()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();

        await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var again = () => users.CreateAsync(
            new CreateUserCommand("priya", "Priya Two", "counter2", Role.Cashier));

        await again.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*already a user called 'priya'*");
    }

    [Fact]
    public async Task FR_1_3_APasswordBelowThePolicyIsRefusedBeforeAnythingIsWritten()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();

        var create = () => users.CreateAsync(new CreateUserCommand("priya", "Priya", "1", Role.Cashier));

        await create.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*at least 4 characters*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM app_user WHERE username = 'priya';"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_1_4_TheListedUsersCarryNoPasswordHash()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();

        await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var listed = await users.ListAsync();

        listed.Should().HaveCount(2);
        listed.Select(user => user.Username).Should().BeEquivalentTo(["owner", "priya"]);

        // UserSummary has no hash property at all - the safest way to keep something off a screen
        // is for the shape the screen is handed not to have it (CLAUDE.md invariant 8).
        typeof(Counterpoint.Application.Abstractions.Persistence.UserSummary)
            .GetProperties()
            .Select(property => property.Name)
            .Should().NotContain("PasswordHash");
    }

    [Fact]
    public async Task AC_17_ACashierIsRefusedByTheServiceItselfNotByAHiddenButton()
    {
        await using var fixture = await SignedInAsOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();
        var authentication = fixture.Resolve<IAuthenticationService>();

        await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        // The same object the composition root resolves, called directly with no screen involved.
        var list = () => users.ListAsync();
        var create = () => users.CreateAsync(
            new CreateUserCommand("mallory", "Mallory", "letmein", Role.Owner));

        await list.Should().ThrowAsync<NotAuthorisedException>();
        await create.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM app_user WHERE username = 'mallory';"))
            .Should().Be(0, "nothing ran, so nothing was written");
    }

    [Fact]
    public async Task AC_17_TheContainerHasNoUndecoratedUserAdministrationServiceToHandOut()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        // The concrete service is built inside the IUserAdministration factory and never
        // registered on its own, so there is nothing for a viewmodel, another service or a test
        // to resolve that would arrive without RoleAuthorisation in front of it. This test can
        // even name the type - Counterpoint.Application grants this assembly an InternalsVisibleTo
        // seam - and the container still has nothing to give it.
        fixture.TryResolve<UserAdministrationService>().Should().BeNull(
            "an undecorated owner-only service must not be resolvable from the container "
            + "(SRS NFR-S2, AC-17)");

        fixture.Resolve<IUserAdministration>().Should().NotBeOfType<UserAdministrationService>(
            "the only IUserAdministration in the container is the role-decorated proxy");
    }

    private static async Task<SaleFixture> SignedInAsOwnerAsync()
    {
        var fixture = await SaleFixture.CreateAsync();

        try
        {
            await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);
            (await fixture.Resolve<IAuthenticationService>().LogInAsync(Owner, OwnerPassword))
                .Succeeded.Should().BeTrue();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }
}
