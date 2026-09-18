using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Integration.Tests.Ui;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// The product tab's master/variant/unit dialogs (SRS FR-2.1-FR-2.8, UI-15, AC-23), task P3-T15's
/// retrofit onto the P3-T11 shared dialog shell, driven end to end over a real encrypted database
/// - the same pattern <c>CategoryTabViewModelDialogTests</c> and <c>ProductTabViewModelUomTests</c>
/// (the still-unmodified, protected P1-T05 test behind this screen's legacy properties) both use.
/// </summary>
public sealed class ProductTabViewModelDialogTests
{
    [Fact]
    public async Task UI_15_NewProductDialogAlwaysAsksForCreateModeWithNoSubjectAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var dialogs = new FakeDialogService();
        var screen = NewScreen(fixture, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewProductDialogCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Create);
        dialogs.EditRequests[0].EntityName.Should().Be("product");
        dialogs.EditRequests[0].SubjectDescription.Should().BeNull(
            "a create dialog has no existing record to name yet");
    }

    [Fact]
    public async Task UI_15_CreatingThroughTheDialogAddsTheProductAndSelectsItAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (pieceId, taxClassId) = await SeedReferenceDataAsync(fixture);
        var dialogs = new FakeDialogService
        {
            ConfigureContent = content =>
            {
                var edit = (ProductEditViewModel)content;
                edit.Code = "HAMMER-1";
                edit.Name = "Hammer";
                edit.SelectedBaseUom = edit.BaseUomOptions.First(o => o.Id == pieceId);
                edit.SelectedTaxClass = edit.TaxClassOptions.First(o => o.Id == taxClassId);
            },
        };
        var screen = NewScreen(fixture, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);

        await screen.NewProductDialogCommand.ExecuteAsync(null);

        screen.Items.Should().ContainSingle(item => item.Code == "HAMMER-1");
        screen.Status.Should().Be("Hammer created.");
        screen.SelectedItem.Should().NotBeNull("the newly created product should be selected");
        screen.SelectedItem!.Code.Should().Be("HAMMER-1");

        (await fixture.CountAsync("SELECT COUNT(*) FROM product WHERE code = 'HAMMER-1';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task UI_15_EditProductDialogNamesTheOriginalSelectedRecordAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (pieceId, taxClassId) = await SeedReferenceDataAsync(fixture);
        var products = fixture.Resolve<IProductMaintenance>();
        var createdId = await products.CreateAsync(new SaveProductCommand(
            "HAMMER-1", "Hammer", null, null, null, pieceId, ProductType.Standard, taxClassId,
            null, false, null, null, null));

        var dialogs = new FakeDialogService();
        var screen = NewScreen(fixture, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        await screen.EditProductDialogCommand.ExecuteAsync(null);

        dialogs.EditRequests.Should().ContainSingle();
        dialogs.EditRequests[0].Mode.Should().Be(DialogMode.Edit);
        dialogs.EditRequests[0].EntityName.Should().Be("product");
        dialogs.EditRequests[0].SubjectDescription.Should().Be("Hammer");
    }

    [Fact]
    public async Task UI_15_VariantAndUnitDialogsActOnTheSelectedProductAndRemoveConfirmsFirstAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (pieceId, taxClassId) = await SeedReferenceDataAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "bx", 0));

        var products = fixture.Resolve<IProductMaintenance>();
        var createdId = await products.CreateAsync(new SaveProductCommand(
            "HAMMER-1", "Hammer", null, null, null, pieceId, ProductType.Standard, taxClassId,
            null, false, null, null, null));

        var dialogs = new FakeDialogService
        {
            ConfigureContent = content =>
            {
                if (content is ProductVariantEditViewModel variant)
                {
                    variant.Sku = "HAMMER-1-RED";
                    variant.PriceText = "500";
                }
                else if (content is ProductUomOptionEditViewModel uomOption)
                {
                    uomOption.SelectedUom = uomOption.AddableUomOptions.First(o => o.Id == boxId);
                    uomOption.FactorText = "12";
                }
            },
        };
        var screen = NewScreen(fixture, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        // OnSelectedItemChanged kicks off LoadSelectedProductAsync fire-and-forget; wait for the
        // addable-unit list it populates before the test drives the dialogs (the same pattern
        // ProductTabViewModelUomTests already uses for this screen).
        for (var attempt = 0; attempt < 200 && screen.AddableUomOptions.Count == 0; attempt++)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        await screen.NewVariantDialogCommand.ExecuteAsync(null);
        screen.Variants.Should().ContainSingle(v => v.Sku == "HAMMER-1-RED");
        screen.Status.Should().Be("Variant HAMMER-1-RED created.");

        await screen.AddUomOptionDialogCommand.ExecuteAsync(null);
        screen.UomOptions.Should().ContainSingle(o => o.UomId == boxId);
        screen.Status.Should().Be("Unit added.");

        screen.SelectedUomOption = screen.UomOptions.Single(o => o.UomId == boxId);
        await screen.RemoveUomOptionCommand.ExecuteAsync(null);

        dialogs.DeleteRequests.Should().ContainSingle();
        dialogs.DeleteRequests[0].EntityName.Should().Be("unit");
        dialogs.DeleteRequests[0].SubjectDescription.Should().Be("bx");
        screen.UomOptions.Should().NotContain(o => o.UomId == boxId);
        screen.Status.Should().Be("Unit removed.");
    }

    [Fact]
    public async Task UI_05_DeclinedRemoveConfirmationLeavesTheUnitInPlaceAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (pieceId, taxClassId) = await SeedReferenceDataAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "bx", 0));

        var products = fixture.Resolve<IProductMaintenance>();
        var createdId = await products.CreateAsync(new SaveProductCommand(
            "HAMMER-1", "Hammer", null, null, null, pieceId, ProductType.Standard, taxClassId,
            null, false, null, null, null));

        var dialogs = new FakeDialogService
        {
            ConfigureContent = content =>
            {
                if (content is ProductUomOptionEditViewModel uomOption)
                {
                    uomOption.SelectedUom = uomOption.AddableUomOptions.First(o => o.Id == boxId);
                    uomOption.FactorText = "12";
                }
            },
        };
        var screen = NewScreen(fixture, dialogs);
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.SelectedItem = screen.Items.Single(item => item.Id == createdId);

        for (var attempt = 0; attempt < 200 && screen.AddableUomOptions.Count == 0; attempt++)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        await screen.AddUomOptionDialogCommand.ExecuteAsync(null);
        screen.SelectedUomOption = screen.UomOptions.Single(o => o.UomId == boxId);

        dialogs.ConfirmDeletes = false;
        await screen.RemoveUomOptionCommand.ExecuteAsync(null);

        screen.UomOptions.Should().ContainSingle(o => o.UomId == boxId,
            "the operator declined the confirmation, so nothing should have been removed");
    }

    private static ProductTabViewModel NewScreen(SaleFixture fixture, FakeDialogService dialogs) =>
        new(
            fixture.Resolve<IProductMaintenance>(),
            fixture.Resolve<ICategoryMaintenance>(),
            fixture.Resolve<IBrandMaintenance>(),
            fixture.Resolve<IUomMaintenance>(),
            fixture.Resolve<ITaxClassMaintenance>(),
            dialogs);

    private static async Task<(long PieceId, long TaxClassId)> SeedReferenceDataAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<IUomMaintenance>();
        var pieceId = await uoms.CreateAsync(new SaveUomCommand("Test unit piece", "pc", 0));

        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var taxClassId = await taxClasses.CreateAsync(new SaveTaxClassCommand("Test exempt", TaxRate.Zero));

        return (pieceId, taxClassId);
    }
}
