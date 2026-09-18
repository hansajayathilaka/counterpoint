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
/// The category tab (SRS FR-2.20), task P3-T11's proof-of-concept for the shared add/edit/delete
/// dialog (SRS UI-05, UI-06, UI-15, AC-23), driven end to end over a real encrypted database - the
/// same pattern <c>SettingsScreenTests</c> and <c>ProductTabViewModelUomTests</c> use, because a
/// window cannot be opened in CI (<see cref="SaleFixture"/>'s own remarks).
/// </summary>
public sealed class CategoryTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewAlwaysAsksForCreateModeWithNoSubjectRegardlessOfWhatIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = new CategoryTabViewModel(fixture.Resolve<ICategoryMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("category");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull(
            "a create dialog has no existing record to name yet");
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheCategoryAndRefreshesTheListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService
        {
            // Stands in for what the operator would type into CategoryEditView before Save.
            ConfigureContent = content => ((CategoryEditViewModel)content).Name = "Fasteners",
        };
        var screen = new CategoryTabViewModel(fixture.Resolve<ICategoryMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Name == "Fasteners");
        screen.Status.Should().Be("Fasteners created.");

        // Through the real Application-layer service, not a shortcut - the same database
        // ProductTabViewModelUomTests and SettingsScreenTests already prove against.
        (await fixture.CountAsync("SELECT COUNT(*) FROM category WHERE name = 'Fasteners';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditAlwaysNamesTheOriginalSelectedRecordEvenIfTheDialogChangesItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var createdId = await categories.CreateAsync(new SaveCategoryCommand("Fasteners", null));

        var dialogs = new FakeDialogService();
        var screen = new CategoryTabViewModel(categories, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("category");
        dialogs.EditRequests[0].SubjectDescription.Should().Be(
            "Fasteners",
            "the header must name the record that was selected when Edit was pressed, driven by "
            + "the explicit subject passed to IDialogService - never inferred afterwards from "
            + "whatever the dialog's own Name field ends up holding");
    }

    [Fact]
    public async Task UI_05_DeleteNamesTheSpecificRecordBeforeAnythingHappensAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var createdId = await categories.CreateAsync(new SaveCategoryCommand("Fasteners", null));

        var dialogs = new FakeDialogService { ConfirmDeletes = false };
        var screen = new CategoryTabViewModel(categories, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("category");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("Fasteners");

        // The operator declined, through the shared confirmation shell - nothing was deleted.
        screen.Items.Should().ContainSingle(item => item.Id == createdId);
    }

    [Fact]
    public async Task UI_05_ConfirmingDeleteThroughTheSharedShellRemovesTheCategoryAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var createdId = await categories.CreateAsync(new SaveCategoryCommand("Fasteners", null));

        var dialogs = new FakeDialogService { ConfirmDeletes = true };
        var screen = new CategoryTabViewModel(categories, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        screen.Items.Should().NotContain(item => item.Id == createdId);
        screen.Status.Should().Be("Fasteners deleted.");
    }
}
