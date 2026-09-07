using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The sign-in and user-management screens, driven from their viewmodels (SRS FR-1.1, FR-1.4,
/// UI-06, AC-17).
/// </summary>
/// <remarks>
/// The viewmodels are exercised directly rather than through a window, because a window cannot be
/// opened in CI. Everything below them - the Application services, the role decorator, the SQLite
/// adapters and the real encrypted file - is the production wiring, composed by
/// <see cref="SaleFixture"/> exactly as <c>Counterpoint.App</c> composes it.
/// </remarks>
public sealed class LoginScreenTests
{
    [Fact]
    public async Task FR_1_1_TheFirstRunBranchSetsTheOwnerPasswordAndSignsStraightIn()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var screen = Login(fixture);

        await screen.LoadCommand.ExecuteAsync(null);

        screen.SetupRequired.Should().BeTrue("a fresh database has no usable credential");
        screen.Username.Should().Be("owner");
        screen.Status.Should().Contain("no owner password yet");

        var signedIn = 0;
        screen.SignedIn += (_, _) => signedIn++;

        screen.Password = "till2026";
        screen.ConfirmPassword = "till2026";
        await screen.SetOwnerPasswordCommand.ExecuteAsync(null);

        signedIn.Should().Be(1);
        screen.SetupRequired.Should().BeFalse();
        screen.Password.Should().BeEmpty("a password left in a bound property sits in memory");
        fixture.Resolve<ISession>().CurrentUser!.Role.Should().Be(Role.Owner);
    }

    [Fact]
    public async Task UI_06_MistypingTheConfirmationSaysSoAndChangesNothing()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        var screen = Login(fixture);
        await screen.LoadCommand.ExecuteAsync(null);

        screen.Password = "till2026";
        screen.ConfirmPassword = "till2027";
        await screen.SetOwnerPasswordCommand.ExecuteAsync(null);

        screen.Status.Should().Be("The two passwords are not the same. Type them again.");
        screen.SetupRequired.Should().BeTrue();
        (await fixture.Resolve<IInitialOwnerSetup>().IsRequiredAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task UI_06_AWrongPasswordIsASentenceOnTheScreenAndNoSession()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");

        var screen = Login(fixture);
        await screen.LoadCommand.ExecuteAsync(null);

        var signedIn = 0;
        screen.SignedIn += (_, _) => signedIn++;

        screen.Username = "owner";
        screen.Password = "wrong";
        await screen.SignInCommand.ExecuteAsync(null);

        signedIn.Should().Be(0);
        screen.Status.Should().Contain("do not match");
        screen.Password.Should().BeEmpty();
        fixture.Resolve<ISession>().IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task FR_1_1_TheRightPasswordHandsOverToTheSalesScreen()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");

        var screen = Login(fixture);
        await screen.LoadCommand.ExecuteAsync(null);

        var signedIn = 0;
        screen.SignedIn += (_, _) => signedIn++;

        screen.Username = "owner";
        screen.Password = "till2026";
        await screen.SignInCommand.ExecuteAsync(null);

        signedIn.Should().Be(1, "the composition root opens the sales window on this event");
        screen.Status.Should().Be("Signed in.");
    }

    [Fact]
    public async Task FR_1_4_TheOwnerSeesTheUsersButtonAndTheCashierDoesNot()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogInAsync("owner", "till2026");

        Sales(fixture).CanManageUsers.Should().BeTrue();

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        await authentication.LogOutAsync();
        await authentication.LogInAsync("priya", "counter1");

        // A courtesy, not the control - the next test is the control.
        Sales(fixture).CanManageUsers.Should().BeFalse();
    }

    [Fact]
    public async Task AC_17_TheUserScreenRefusesACashierEvenWhenItIsOpenInFrontOfThem()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogInAsync("owner", "till2026");
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));
        await authentication.LogOutAsync();
        await authentication.LogInAsync("priya", "counter1");

        var screen = new UserAdminViewModel(fixture.Resolve<IUserAdministration>());

        await screen.RefreshCommand.ExecuteAsync(null);
        screen.Users.Should().BeEmpty("the list itself is owner-only");
        screen.Status.Should().Contain("cashier");

        screen.NewUsername = "mallory";
        screen.NewDisplayName = "Mallory";
        screen.NewPassword = "letmein";
        screen.NewUserIsOwner = true;
        await screen.CreateCommand.ExecuteAsync(null);

        screen.Status.Should().Contain("cashier");
        (await fixture.CountAsync("SELECT COUNT(*) FROM app_user WHERE username = 'mallory';"))
            .Should().Be(0, "the Application layer refused; the screen only reported it");
    }

    [Fact]
    public async Task FR_1_4_TheOwnerCanCreateAndTurnOffAUserFromTheScreen()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");
        await fixture.Resolve<IAuthenticationService>().LogInAsync("owner", "till2026");

        var screen = new UserAdminViewModel(fixture.Resolve<IUserAdministration>());
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.Users.Should().ContainSingle().Which.Username.Should().Be("owner");

        screen.NewUsername = "priya";
        screen.NewDisplayName = "Priya";
        screen.NewPassword = "counter1";
        await screen.CreateCommand.ExecuteAsync(null);

        screen.Users.Should().HaveCount(2);
        screen.Status.Should().Be("priya can now sign in.");

        screen.SelectedUser = screen.Users.Single(user => user.Username == "priya");
        await screen.ToggleActiveCommand.ExecuteAsync(null);

        screen.Status.Should().Be("priya is turned off.");
        (await fixture.ScalarAsync("SELECT active FROM app_user WHERE username = 'priya';"))
            .Should().Be("0");
    }

    [Fact]
    public async Task UI_06_TheLastOwnerRuleReachesTheScreenAsASentence()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");
        await fixture.Resolve<IAuthenticationService>().LogInAsync("owner", "till2026");

        var screen = new UserAdminViewModel(fixture.Resolve<IUserAdministration>());
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.SelectedUser = screen.Users.Single();
        await screen.ToggleActiveCommand.ExecuteAsync(null);

        screen.Status.Should().Contain("only owner account left");
        (await fixture.ScalarAsync("SELECT active FROM app_user WHERE username = 'owner';"))
            .Should().Be("1");
    }

    private static LoginViewModel Login(SaleFixture fixture) => new(
        fixture.Resolve<IAuthenticationService>(),
        fixture.Resolve<IInitialOwnerSetup>());

    private static SalesViewModel Sales(SaleFixture fixture) => new(
        fixture.Resolve<IScanItem>(),
        fixture.Resolve<IQuoteSale>(),
        fixture.Resolve<ICompleteSale>(),
        fixture.Resolve<ITillSessionProvider>(),
        fixture.Resolve<ISession>(),
        fixture.Resolve<TimeProvider>());
}
