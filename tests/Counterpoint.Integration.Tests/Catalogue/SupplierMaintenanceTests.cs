using System;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>The owner's supplier maintenance (SRS FR-6.5).</summary>
public sealed class SupplierMaintenanceTests
{
    [Fact]
    public async Task FR_6_5_ASupplierCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();

        var id = await suppliers.CreateAsync(
            new SaveSupplierCommand("ABC Hardware Distributors", "Nimal", "011-2233445", "Colombo", "TAX-1", "30 days"));

        (await fixture.ScalarAsync($"SELECT contact FROM supplier WHERE id = {id};")).Should().Be("Nimal");

        await suppliers.UpdateAsync(
            id, new SaveSupplierCommand("ABC Hardware Distributors", "Kamal", "011-2233445", "Colombo", "TAX-1", "60 days"));
        (await fixture.ScalarAsync($"SELECT contact FROM supplier WHERE id = {id};")).Should().Be("Kamal");

        await suppliers.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM supplier WHERE id = {id};")).Should().Be("0");

        await suppliers.ReactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM supplier WHERE id = {id};")).Should().Be("1");
    }

    [Fact]
    public async Task FR_2_1_DeletingASupplierLinkedToAProductIsRefusedButDeactivatingSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();

        var id = await suppliers.CreateAsync(
            new SaveSupplierCommand("Linked Supplier", null, null, null, null, null));
        await CatalogueTestProducts.CreateAsync(fixture, supplierId: id);

        var delete = () => suppliers.DeleteAsync(id);
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");

        await suppliers.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM supplier WHERE id = {id};")).Should().Be("0");
    }

    [Fact]
    public async Task AnUnlinkedSupplierCanBeDeletedOutright()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();

        var id = await suppliers.CreateAsync(new SaveSupplierCommand("Unused Supplier", null, null, null, null, null));

        await suppliers.DeleteAsync(id);

        (await fixture.CountAsync($"SELECT COUNT(*) FROM supplier WHERE id = {id};")).Should().Be(0);
    }
}
