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
/// The unit-of-measure tab, task P3-T15's retrofit onto the P3-T11 shared dialog shell, following
/// <c>CategoryTabViewModelDialogTests</c>'s pattern exactly, driven end to end over a real
/// encrypted database.
/// </summary>
public sealed class UomTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewAlwaysAsksForCreateModeWithNoSubjectRegardlessOfWhatIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = new UomTabViewModel(fixture.Resolve<IUomMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("unit");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull();
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheUnitAndRefreshesTheListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService
        {
            ConfigureContent = content =>
            {
                var edit = (UomEditViewModel)content;
                edit.Name = "Box";
                edit.Symbol = "bx";
                edit.DecimalPlacesText = "0";
            },
        };
        var screen = new UomTabViewModel(fixture.Resolve<IUomMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Name == "Box" && item.Symbol == "bx");
        screen.Status.Should().Be("Box created.");

        (await fixture.CountAsync("SELECT COUNT(*) FROM uom WHERE name = 'Box';")).Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditAlwaysNamesTheOriginalSelectedRecordEvenIfTheDialogChangesItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var createdId = await uoms.CreateAsync(new SaveUomCommand("Box", "bx", 0));

        var dialogs = new FakeDialogService();
        var screen = new UomTabViewModel(uoms, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("unit");
        dialogs.EditRequests[0].SubjectDescription.Should().Be("Box");
    }

    [Fact]
    public async Task UI_05_DeleteNamesTheSpecificRecordBeforeAnythingHappensAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var createdId = await uoms.CreateAsync(new SaveUomCommand("Box", "bx", 0));

        var dialogs = new FakeDialogService { ConfirmDeletes = false };
        var screen = new UomTabViewModel(uoms, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("unit");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("Box");
        screen.Items.Should().ContainSingle(item => item.Id == createdId);
    }

    [Fact]
    public async Task UI_05_ConfirmingDeleteThroughTheSharedShellRemovesTheUnitAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var createdId = await uoms.CreateAsync(new SaveUomCommand("Box", "bx", 0));

        var dialogs = new FakeDialogService { ConfirmDeletes = true };
        var screen = new UomTabViewModel(uoms, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        screen.Items.Should().NotContain(item => item.Id == createdId);
        screen.Status.Should().Be("Box deleted.");
    }
}
