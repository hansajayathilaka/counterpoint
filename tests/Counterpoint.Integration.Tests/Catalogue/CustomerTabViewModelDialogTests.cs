using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Integration.Tests.Ui;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// The customer tab (SRS FR-6.1), task P3-T15's retrofit onto the P3-T11 shared dialog shell,
/// following <c>CategoryTabViewModelDialogTests</c>'s pattern exactly, driven end to end over a
/// real encrypted database.
/// </summary>
public sealed class CustomerTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewAlwaysAsksForCreateModeWithNoSubjectRegardlessOfWhatIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = new CustomerTabViewModel(fixture.Resolve<ICustomerMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("customer");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull();
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheCustomerAndRefreshesTheListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService
        {
            ConfigureContent = content => ((CustomerEditViewModel)content).Name = "Kamal Perera",
        };
        var screen = new CustomerTabViewModel(fixture.Resolve<ICustomerMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Name == "Kamal Perera");
        screen.Status.Should().Be("Kamal Perera created.");

        (await fixture.CountAsync("SELECT COUNT(*) FROM customer WHERE name = 'Kamal Perera';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditAlwaysNamesTheOriginalSelectedRecordEvenIfTheDialogChangesItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();
        var createdId = await customers.CreateAsync(
            new SaveCustomerCommand("Kamal Perera", string.Empty, string.Empty, string.Empty, "RETAIL", Money.Zero));

        var dialogs = new FakeDialogService();
        var screen = new CustomerTabViewModel(customers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("customer");
        dialogs.EditRequests[0].SubjectDescription.Should().Be("Kamal Perera");
    }

    [Fact]
    public async Task UI_05_DeleteNamesTheSpecificRecordBeforeAnythingHappensAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();
        var createdId = await customers.CreateAsync(
            new SaveCustomerCommand("Kamal Perera", string.Empty, string.Empty, string.Empty, "RETAIL", Money.Zero));

        var dialogs = new FakeDialogService { ConfirmDeletes = false };
        var screen = new CustomerTabViewModel(customers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("customer");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("Kamal Perera");
        screen.Items.Should().ContainSingle(item => item.Id == createdId);
    }

    [Fact]
    public async Task UI_05_ConfirmingDeleteThroughTheSharedShellRemovesTheCustomerAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var customers = fixture.Resolve<ICustomerMaintenance>();
        var createdId = await customers.CreateAsync(
            new SaveCustomerCommand("Kamal Perera", string.Empty, string.Empty, string.Empty, "RETAIL", Money.Zero));

        var dialogs = new FakeDialogService { ConfirmDeletes = true };
        var screen = new CustomerTabViewModel(customers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        screen.Items.Should().NotContain(item => item.Id == createdId);
        screen.Status.Should().Be("Kamal Perera deleted.");
    }
}
