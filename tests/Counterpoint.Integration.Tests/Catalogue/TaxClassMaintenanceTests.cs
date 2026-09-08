using System;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>The owner's ongoing tax-class maintenance, after the first-run wizard (Q-02, FR-10.3).</summary>
public sealed class TaxClassMaintenanceTests
{
    [Fact]
    public async Task ATaxClassCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var id = await taxClasses.CreateAsync(new SaveTaxClassCommand("Standard 15%", TaxRate.FromPercent(15m)));
        (await fixture.ScalarAsync($"SELECT rate FROM tax_class WHERE id = {id};")).Should().Be("1500");

        await taxClasses.UpdateAsync(id, new SaveTaxClassCommand("Standard 18%", TaxRate.FromPercent(18m)));
        (await fixture.ScalarAsync($"SELECT rate FROM tax_class WHERE id = {id};")).Should().Be("1800");

        await taxClasses.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM tax_class WHERE id = {id};")).Should().Be("0");

        await taxClasses.ReactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM tax_class WHERE id = {id};")).Should().Be("1");
    }

    [Fact]
    public async Task FR_2_1_DeletingATaxClassWithProductsIsRefusedButDeactivatingSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var id = await taxClasses.CreateAsync(new SaveTaxClassCommand("Zero rated", TaxRate.Zero));
        await CatalogueTestProducts.CreateAsync(fixture, taxClassId: id);

        var delete = () => taxClasses.DeleteAsync(id);
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");

        await taxClasses.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM tax_class WHERE id = {id};")).Should().Be("0");
    }

    [Fact]
    public async Task ADuplicateTaxClassNameIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        await taxClasses.CreateAsync(new SaveTaxClassCommand("Luxury", TaxRate.FromPercent(25m)));

        var again = () => taxClasses.CreateAsync(new SaveTaxClassCommand("Luxury", TaxRate.FromPercent(30m)));
        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already a tax class*");
    }
}
