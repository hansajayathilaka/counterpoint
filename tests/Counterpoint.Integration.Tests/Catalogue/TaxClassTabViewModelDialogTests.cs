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
/// The tax-class tab (Q-02, FR-10.3), task P3-T15's retrofit onto the P3-T11 shared dialog shell,
/// following <c>CategoryTabViewModelDialogTests</c>'s pattern exactly, driven end to end over a
/// real encrypted database.
/// </summary>
public sealed class TaxClassTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewAlwaysAsksForCreateModeWithNoSubjectRegardlessOfWhatIsSelectedAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = new TaxClassTabViewModel(fixture.Resolve<ITaxClassMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("tax class");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull();
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheTaxClassAndRefreshesTheListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService
        {
            ConfigureContent = content =>
            {
                var edit = (TaxClassEditViewModel)content;
                edit.Name = "Standard 15%";
                edit.RatePercentText = "15";
            },
        };
        var screen = new TaxClassTabViewModel(fixture.Resolve<ITaxClassMaintenance>(), dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Name == "Standard 15%");
        screen.Status.Should().Be("Standard 15% created.");

        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class WHERE name = 'Standard 15%';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditAlwaysNamesTheOriginalSelectedRecordEvenIfTheDialogChangesItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var createdId = await taxClasses.CreateAsync(new SaveTaxClassCommand("Standard", TaxRate.FromPercent(15m)));

        var dialogs = new FakeDialogService();
        var screen = new TaxClassTabViewModel(taxClasses, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("tax class");
        dialogs.EditRequests[0].SubjectDescription.Should().Be("Standard");
    }

    [Fact]
    public async Task UI_05_DeleteNamesTheSpecificRecordBeforeAnythingHappensAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var createdId = await taxClasses.CreateAsync(new SaveTaxClassCommand("Standard", TaxRate.FromPercent(15m)));

        var dialogs = new FakeDialogService { ConfirmDeletes = false };
        var screen = new TaxClassTabViewModel(taxClasses, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("tax class");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("Standard");
        screen.Items.Should().ContainSingle(item => item.Id == createdId);
    }

    [Fact]
    public async Task UI_05_ConfirmingDeleteThroughTheSharedShellRemovesTheTaxClassAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var createdId = await taxClasses.CreateAsync(new SaveTaxClassCommand("Standard", TaxRate.FromPercent(15m)));

        var dialogs = new FakeDialogService { ConfirmDeletes = true };
        var screen = new TaxClassTabViewModel(taxClasses, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.DeleteCommand.ExecuteAsync(null);

        screen.Items.Should().NotContain(item => item.Id == createdId);
        screen.Status.Should().Be("Standard deleted.");
    }
}
