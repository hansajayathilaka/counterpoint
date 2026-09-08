using System;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>The owner's brand maintenance (SRS FR-2.21).</summary>
public sealed class BrandMaintenanceTests
{
    [Fact]
    public async Task FR_2_21_ABrandCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();

        var id = await brands.CreateAsync(new SaveBrandCommand("Bosch"));
        (await fixture.ScalarAsync($"SELECT active FROM brand WHERE id = {id};")).Should().Be("1");

        await brands.UpdateAsync(id, new SaveBrandCommand("Bosch Professional"));
        (await fixture.ScalarAsync($"SELECT name FROM brand WHERE id = {id};")).Should().Be("Bosch Professional");

        await brands.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM brand WHERE id = {id};")).Should().Be("0");

        await brands.ReactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM brand WHERE id = {id};")).Should().Be("1");
    }

    [Fact]
    public async Task FR_2_1_DeletingABrandWithProductsIsRefusedButDeactivatingSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();

        var brandId = await brands.CreateAsync(new SaveBrandCommand("Stanley"));
        await CatalogueTestProducts.CreateAsync(fixture, brandId: brandId);

        var delete = () => brands.DeleteAsync(brandId);
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");

        await brands.DeactivateAsync(brandId);
        (await fixture.ScalarAsync($"SELECT active FROM brand WHERE id = {brandId};")).Should().Be("0");
    }

    [Fact]
    public async Task FR_2_21_ADuplicateBrandNameIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var brands = fixture.Resolve<IBrandMaintenance>();

        await brands.CreateAsync(new SaveBrandCommand("Makita"));

        var again = () => brands.CreateAsync(new SaveBrandCommand("Makita"));
        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already a brand*");
    }
}
