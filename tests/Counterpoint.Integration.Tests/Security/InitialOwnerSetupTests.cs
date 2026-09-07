using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The one-time step that opens a brand new database (SRS FR-1.1, FR-1.3).
/// </summary>
public sealed class InitialOwnerSetupTests
{
    [Fact]
    public async Task FR_1_3_AFreshDatabaseHasNoUsableCredentialAtAll()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        // The seeded owner carries a placeholder, not an Argon2id string
        // (docs/01_DATA_MODEL.md §11). There is no default password anywhere in this system.
        (await fixture.ScalarAsync("SELECT password_hash FROM app_user WHERE username = 'owner';"))
            .Should().Be("!");

        (await fixture.Resolve<IInitialOwnerSetup>().IsRequiredAsync()).Should().BeTrue();

        (await fixture.Resolve<IAuthenticationService>().LogInAsync("owner", "owner"))
            .Succeeded.Should().BeFalse();
        (await fixture.Resolve<IAuthenticationService>().LogInAsync("owner", "!"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task FR_1_1_SettingTheFirstOwnerPasswordOpensTheTill()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");

        (await fixture.Resolve<IInitialOwnerSetup>().IsRequiredAsync()).Should().BeFalse();
        (await fixture.Resolve<IAuthenticationService>().LogInAsync("owner", "till2026"))
            .Succeeded.Should().BeTrue();

        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{SecurityAuditActions.OwnerPasswordInitialised}';"))
            .Should().Be(1, "when the shop's first credential was created is worth recording");
    }

    [Fact]
    public async Task FR_1_4_ItClosesForGoodOnceTheShopHasACredential()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var setup = fixture.Resolve<IInitialOwnerSetup>();

        await setup.CompleteAsync("owner", "till2026");

        // Otherwise it would be a way round IUserAdministration and its owner-only check: anyone
        // at the keyboard could take the shop's own account off it.
        var again = () => setup.CompleteAsync("owner", "letmein");

        await again.Should().ThrowAsync<NotAuthorisedException>().WithMessage("*already has an owner password*");

        (await fixture.Resolve<IAuthenticationService>().LogInAsync("owner", "letmein"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task FR_1_3_TheFirstPasswordStillHasToMeetThePolicy()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var complete = () => fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "1");

        await complete.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*at least 4 characters*");
    }

    [Fact]
    public async Task AnAccountThatIsNotAnActiveOwnerCannotBeOpenedThisWay()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var complete = () => fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("nobody", "till2026");

        await complete.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*no active owner account*");
    }
}
