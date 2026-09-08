using System;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>The owner's unit-of-measure maintenance.</summary>
public sealed class UomMaintenanceTests
{
    [Fact]
    public async Task AUnitCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var id = await uoms.CreateAsync(new SaveUomCommand("Coil", "coil", 0));
        (await fixture.ScalarAsync($"SELECT symbol FROM uom WHERE id = {id};")).Should().Be("coil");
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {id};")).Should().Be("1");

        await uoms.UpdateAsync(id, new SaveUomCommand("Coil", "col", 0));
        (await fixture.ScalarAsync($"SELECT symbol FROM uom WHERE id = {id};")).Should().Be("col");

        await uoms.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {id};")).Should().Be("0");

        await uoms.ReactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {id};")).Should().Be("1");
    }

    [Fact]
    public async Task DeactivatingAndReactivatingAreIdempotent()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var id = await uoms.CreateAsync(new SaveUomCommand("Sheet", "sht", 0));

        // Already active: reactivating is a no-op, not an error.
        await uoms.ReactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {id};")).Should().Be("1");

        await uoms.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {id};")).Should().Be("0");

        // Already inactive: deactivating again is a no-op, not an error.
        await uoms.DeactivateAsync(id);
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {id};")).Should().Be("0");

        // A no-op does not write a second audit row.
        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{CatalogueAuditActions.Deactivated}' "
            + $"AND entity_type = '{CatalogueAuditActions.UomEntityType}' AND entity_id = {id};"))
            .Should().Be(1);
    }

    [Fact]
    public async Task EveryChangeIsRecordedInTheAuditTrailAndRoundTripsThroughListAsync()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var id = await uoms.CreateAsync(new SaveUomCommand("Drum", "drm", 0));
        await uoms.UpdateAsync(id, new SaveUomCommand("Drum", "dr", 0));
        await uoms.DeactivateAsync(id);
        await uoms.ReactivateAsync(id);

        var ownerId = fixture.Resolve<ISession>().CurrentUser!.Id;

        foreach (var action in new[]
                 {
                     CatalogueAuditActions.Created,
                     CatalogueAuditActions.Updated,
                     CatalogueAuditActions.Deactivated,
                     CatalogueAuditActions.Reactivated,
                 })
        {
            (await fixture.CountAsync(
                $"SELECT COUNT(*) FROM audit_log WHERE action = '{action}' "
                + $"AND entity_type = '{CatalogueAuditActions.UomEntityType}' "
                + $"AND user_id = {ownerId} AND entity_id = {id};"))
                .Should().Be(1, "{0} is recorded against the owner who did it", action);
        }

        var listed = await uoms.ListAsync();
        var row = listed.Should().ContainSingle(uom => uom.Id == id).Subject;
        row.Name.Should().Be("Drum");
        row.Symbol.Should().Be("dr");
        row.Active.Should().BeTrue();
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
    public async Task FR_2_1_DeletingAUnitThatAProductReferencesIsRefusedButDeactivatingSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var uoms = fixture.Resolve<IUomMaintenance>();

        var usedId = await uoms.CreateAsync(new SaveUomCommand("Roll", "roll", 0));
        await CatalogueTestProducts.CreateAsync(fixture, uomId: usedId);

        var delete = () => uoms.DeleteAsync(usedId);
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be deleted*");

        await uoms.DeactivateAsync(usedId);
        (await fixture.ScalarAsync($"SELECT active FROM uom WHERE id = {usedId};")).Should().Be("0");

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
