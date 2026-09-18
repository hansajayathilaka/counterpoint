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
/// The supplier tab (SRS FR-6.5), task P3-T15's retrofit onto the P3-T11 shared dialog shell,
/// following <c>CategoryTabViewModelDialogTests</c>'s pattern exactly, driven end to end over a
/// real encrypted database.
/// </summary>
public sealed class SupplierTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewAlwaysAsksForCreateModeWithNoSubjectRegardlessOfWhatIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = new SupplierTabViewModel(fixture.Resolve<ISupplierMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("supplier");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull();
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheSupplierAndRefreshesTheListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService
        {
            ConfigureContent = content => ((SupplierEditViewModel)content).Name = "Acme Tools",
        };
        var screen = new SupplierTabViewModel(fixture.Resolve<ISupplierMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Name == "Acme Tools");
        screen.Status.Should().Be("Acme Tools created.");

        (await fixture.CountAsync("SELECT COUNT(*) FROM supplier WHERE name = 'Acme Tools';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditAlwaysNamesTheOriginalSelectedRecordEvenIfTheDialogChangesItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();
        var createdId = await suppliers.CreateAsync(
            new SaveSupplierCommand("Acme Tools", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));

        var dialogs = new FakeDialogService();
        var screen = new SupplierTabViewModel(suppliers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("supplier");
        dialogs.EditRequests[0].SubjectDescription.Should().Be("Acme Tools");
    }

    [Fact]
    public async Task UI_05_DeleteNamesTheSpecificRecordBeforeAnythingHappensAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();
        var createdId = await suppliers.CreateAsync(
            new SaveSupplierCommand("Acme Tools", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));

        var dialogs = new FakeDialogService { ConfirmDeletes = false };
        var screen = new SupplierTabViewModel(suppliers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("supplier");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("Acme Tools");
        screen.Items.Should().ContainSingle(item => item.Id == createdId);
    }

    [Fact]
    public async Task UI_05_ConfirmingDeleteThroughTheSharedShellRemovesTheSupplierAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();
        var createdId = await suppliers.CreateAsync(
            new SaveSupplierCommand("Acme Tools", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));

        var dialogs = new FakeDialogService { ConfirmDeletes = true };
        var screen = new SupplierTabViewModel(suppliers, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        screen.Items.Should().NotContain(item => item.Id == createdId);
        screen.Status.Should().Be("Acme Tools deleted.");
    }
}
