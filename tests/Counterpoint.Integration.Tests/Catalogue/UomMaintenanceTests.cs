using System;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// The owner's unit-of-measure maintenance. No "turn off": the <c>uom</c> table has no
/// <c>active</c> column (see the remarks on <c>UomRecord</c> and the P1-T04 task report).
/// </summary>
public sealed class UomMaintenanceTests
{
    [Fact]
    public async Task AUnitCanBeCreatedAndEdited()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var id = await uoms.CreateAsync(new SaveUomCommand("Coil", "coil", 0));
        (await fixture.ScalarAsync($"SELECT symbol FROM uom WHERE id = {id};")).Should().Be("coil");

        await uoms.UpdateAsync(id, new SaveUomCommand("Coil", "col", 0));
        (await fixture.ScalarAsync($"SELECT symbol FROM uom WHERE id = {id};")).Should().Be("col");
    }

    [Fact]
    public async Task AnOutOfRangeDecimalPlacesValueIsRejected()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var create = () => uoms.CreateAsync(new SaveUomCommand("Bad", "bad", 5));

        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*0 and 4*");
    }

    [Fact]
    public async Task DeletingAUnitThatAProductReferencesIsRefusedButAnUnusedOneCanBeDeleted()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var usedId = await uoms.CreateAsync(new SaveUomCommand("Roll", "roll", 0));
        await CatalogueTestProducts.CreateAsync(fixture, uomId: usedId);

        var delete = () => uoms.DeleteAsync(usedId);
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");

        var unusedId = await uoms.CreateAsync(new SaveUomCommand("Bundle", "bdl", 0));
        await uoms.DeleteAsync(unusedId);

        (await fixture.CountAsync($"SELECT COUNT(*) FROM uom WHERE id = {unusedId};")).Should().Be(0);
    }

    [Fact]
    public async Task ADuplicateUnitNameIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        await uoms.CreateAsync(new SaveUomCommand("Packet", "pkt", 0));

        var again = () => uoms.CreateAsync(new SaveUomCommand("Packet", "pk", 0));
        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already a unit*");
    }
}
