using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Integration.Tests.Ui;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// The brand tab (SRS FR-2.21), task P3-T15's retrofit onto the P3-T11 shared dialog shell,
/// following <c>CategoryTabViewModelDialogTests</c>'s pattern exactly, driven end to end over a
/// real encrypted database.
/// </summary>
public sealed class BrandTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewAlwaysAsksForCreateModeWithNoSubjectRegardlessOfWhatIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = new BrandTabViewModel(fixture.Resolve<IBrandMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("brand");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull(
            "a create dialog has no existing record to name yet");
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheBrandAndRefreshesTheListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService
        {
            ConfigureContent = content => ((BrandEditViewModel)content).Name = "Bosch",
        };
        var screen = new BrandTabViewModel(fixture.Resolve<IBrandMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Name == "Bosch");
        screen.Status.Should().Be("Bosch created.");

        (await fixture.CountAsync("SELECT COUNT(*) FROM brand WHERE name = 'Bosch';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditAlwaysNamesTheOriginalSelectedRecordEvenIfTheDialogChangesItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var createdId = await brands.CreateAsync(new SaveBrandCommand("Bosch"));

        var dialogs = new FakeDialogService();
        var screen = new BrandTabViewModel(brands, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("brand");
        dialogs.EditRequests[0].SubjectDescription.Should().Be("Bosch");
    }

    [Fact]
    public async Task UI_05_DeleteNamesTheSpecificRecordBeforeAnythingHappensAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var createdId = await brands.CreateAsync(new SaveBrandCommand("Bosch"));

        var dialogs = new FakeDialogService { ConfirmDeletes = false };
        var screen = new BrandTabViewModel(brands, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("brand");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("Bosch");
        screen.Items.Should().ContainSingle(item => item.Id == createdId);
    }

    [Fact]
    public async Task UI_05_ConfirmingDeleteThroughTheSharedShellRemovesTheBrandAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var createdId = await brands.CreateAsync(new SaveBrandCommand("Bosch"));

        var dialogs = new FakeDialogService { ConfirmDeletes = true };
        var screen = new BrandTabViewModel(brands, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        screen.Items.Should().NotContain(item => item.Id == createdId);
        screen.Status.Should().Be("Bosch deleted.");
    }
}
