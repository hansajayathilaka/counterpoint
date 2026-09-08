using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// A blank or non-positive "conversion factor" box on the product unit-of-measure grid must
/// never leave the owner without an explanation (SRS UI-06) and must never let an unhandled
/// <see cref="System.ArgumentOutOfRangeException"/> escape <see cref="ProductTabViewModel"/>'s
/// command handlers (CLAUDE.md invariant 7's spirit: nothing in a maintenance screen may blow up
/// in the owner's hands).
/// </summary>
public sealed class ProductTabViewModelUomTests
{
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public async Task AC_UomFactor_BlankOrZeroFactorLeavesAPlainLanguageStatusInsteadOfThrowingAsync(string factorText)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (screen, boxUomId) = await OpenWithSavedProductAsync(fixture);

        screen.SelectedUomToAdd = new PickerOption(boxUomId, "Box (bx)");
        screen.UomFactorText = factorText;

        var act = () => screen.AddUomOptionCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync(
            "a blank or zero conversion factor is an owner mistake, not a crash");

        screen.Status.Should().NotBeNullOrWhiteSpace(
            "the owner must see why the unit was not added, not silence");
        screen.Status.Should().NotBe(
            "Unit added.", "nothing should have been saved with an invalid factor");
        screen.Busy.Should().BeFalse();

        // Confirm the design point: nothing was actually written for the invalid factor.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM product_uom WHERE uom_id = " + boxUomId + ";"))
            .Should().Be(0, "AddUomOptionAsync must not have reached the Application layer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public async Task AC_UomFactor_UpdateWithBlankOrZeroFactorLeavesAPlainLanguageStatusInsteadOfThrowingAsync(string factorText)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (screen, boxUomId) = await OpenWithSavedProductAsync(fixture);

        // Add a valid unit first, so there is something to edit.
        screen.SelectedUomToAdd = new PickerOption(boxUomId, "Box (bx)");
        screen.UomFactorText = "12";
        await screen.AddUomOptionCommand.ExecuteAsync(null);
        screen.Status.Should().Be("Unit added.");

        var added = screen.UomOptions.Single(o => o.UomId == boxUomId);
        screen.SelectedUomOption = added;
        screen.UomFactorText = factorText;

        var act = () => screen.UpdateUomOptionCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync(
            "a blank or zero conversion factor is an owner mistake, not a crash");

        screen.Status.Should().NotBeNullOrWhiteSpace(
            "the owner must see why the unit was not updated, not silence");
        screen.Status.Should().NotBe("Unit updated.");
        screen.Busy.Should().BeFalse();

        // The original, valid factor must survive an update that was rejected.
        (await fixture.ScalarAsync(
            "SELECT conversion_factor FROM product_uom WHERE id = " + added.Id + ";"))
            .Should().Be((12m * UomConversion.FactorScale).ToString("0"));
    }

    /// <summary>
    /// Builds a saved product with its base unit, a second addable unit, and a viewmodel that has
    /// selected that product - the state the grid is in whenever the owner is looking at the unit
    /// editor at all.
    /// </summary>
    private static async Task<(ProductTabViewModel Screen, long BoxUomId)> OpenWithSavedProductAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<IUomMaintenance>();
        var pieceId = await uoms.CreateAsync(new SaveUomCommand("Test piece", "pc", 0));
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Test box", "bx", 0));

        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var taxClassId = await taxClasses.CreateAsync(
            new SaveTaxClassCommand("Test standard", TaxRate.Zero));

        var screen = new ProductTabViewModel(
            fixture.Resolve<IProductMaintenance>(),
            fixture.Resolve<ICategoryMaintenance>(),
            fixture.Resolve<IBrandMaintenance>(),
            uoms,
            taxClasses);

        await screen.RefreshCommand.ExecuteAsync(null);
        screen.New();

        screen.Code = "HAMMER-1";
        screen.Name = "Hammer";
        screen.SelectedBaseUom = screen.BaseUomOptions.First(o => o.Id == pieceId);
        screen.SelectedTaxClass = screen.TaxClassOptions.First(o => o.Id == taxClassId);

        await screen.SaveCommand.ExecuteAsync(null);
        screen.SelectedItem.Should().NotBeNull("the product must have saved before the unit grid can be used");

        // OnSelectedItemChanged kicks off LoadSelectedProductAsync fire-and-forget; wait for the
        // addable-unit list it populates before the test drives the grid.
        for (var attempt = 0; attempt < 200 && screen.AddableUomOptions.Count == 0; attempt++)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        screen.AddableUomOptions.Should().Contain(o => o.Id == boxId);

        return (screen, boxId);
    }
}
