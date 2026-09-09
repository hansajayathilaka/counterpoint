using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Import;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Import;

/// <summary>
/// <see cref="ICatalogueImportService.ListMappingProfilesAsync"/>,
/// <see cref="ICatalogueImportService.SaveMappingProfileAsync"/> and
/// <see cref="ICatalogueImportService.DeleteMappingProfileAsync"/> - a named mapping profile is
/// stored as one JSON blob under the <c>app_setting</c> key <c>import.mapping_profiles</c>
/// (docs/03_PHASE_1_core_trading.md P1-T13 "remembered as a named profile", SRS FR-2.22).
/// </summary>
/// <remarks>
/// A real SQLite file per test through <see cref="SaleFixture"/>, never the EF in-memory
/// provider (CLAUDE.md, the Infrastructure testing rule) - a profile round trip here is really a
/// round trip through <c>ISettingStore</c> and its JSON column.
/// </remarks>
public sealed class CatalogueImportMappingProfileTests
{
    [Fact]
    public async Task AFreshInstallWithNoSavedProfilesReturnsAnEmptyListNotAnError()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var profiles = await import.ListMappingProfilesAsync();

        profiles.Should().NotBeNull();
        profiles.Should().BeEmpty("nothing has been saved yet, which is not the same as an error");
    }

    [Fact]
    public async Task SavingAProfileUnderANewNameAddsIt()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var mapping = SupplierAMapping();
        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier A", mapping));

        var profiles = await import.ListMappingProfilesAsync();

        profiles.Should().ContainSingle(p => p.Name == "Supplier A");

        // Save a second, differently-named profile: the first must still be there, not replaced.
        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier B", SupplierBMapping()));

        var afterSecond = await import.ListMappingProfilesAsync();
        afterSecond.Select(p => p.Name).Should().BeEquivalentTo(["Supplier A", "Supplier B"]);
    }

    [Fact]
    public async Task SavingAProfileUnderAnExistingNameReplacesItRatherThanDuplicatingIt()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier A", SupplierAMapping()));
        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier B", SupplierBMapping()));

        var revisedMapping = SupplierAMapping() with { Price = "Unit Price", Cost = "Unit Cost" };
        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier A", revisedMapping));

        var profiles = await import.ListMappingProfilesAsync();

        profiles.Should().HaveCount(2, "re-saving \"Supplier A\" must replace it, not add a second one");
        profiles.Where(p => p.Name == "Supplier A").Should().ContainSingle();
        profiles.Single(p => p.Name == "Supplier A").Mapping.Should().Be(revisedMapping,
            "the stored mapping must be the revised one, not the original");
    }

    [Fact]
    public async Task DeletingAProfileRemovesItAndLeavesTheOthers()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier A", SupplierAMapping()));
        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier B", SupplierBMapping()));
        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier C", SupplierAMapping()));

        await import.DeleteMappingProfileAsync("Supplier B");

        var profiles = await import.ListMappingProfilesAsync();

        profiles.Select(p => p.Name).Should().BeEquivalentTo(["Supplier A", "Supplier C"]);
    }

    [Fact]
    public async Task DeletingAProfileThatDoesNotExistSucceedsWithoutChangingAnything()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        await import.SaveMappingProfileAsync(new ImportMappingProfile("Supplier A", SupplierAMapping()));

        var act = () => import.DeleteMappingProfileAsync("Nobody Saved This");
        await act.Should().NotThrowAsync("deleting a profile that never existed always succeeds");

        var profiles = await import.ListMappingProfilesAsync();
        profiles.Should().ContainSingle(p => p.Name == "Supplier A");
    }

    [Fact]
    public async Task AProfileSurvivesARoundTripWithTheSameNameAndTheSameMappingShape()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var mapping = new ImportColumnMapping(
            Code: "Item Code",
            Name: "Item Description",
            NameAlt: "Local Name",
            Category: "Group",
            Brand: "Manufacturer",
            Unit: "UOM",
            Type: "Kind",
            TaxClass: "VAT Class",
            Location: "Bin",
            NonReturnable: "No Return",
            WarrantyDays: "Warranty",
            Notes: "Remarks",
            Barcode: "EAN",
            Price: "Retail",
            Cost: "Landed Cost",
            Qty: "On Hand");

        var saved = new ImportMappingProfile("Round Trip Supplier", mapping);
        await import.SaveMappingProfileAsync(saved);

        var reloaded = (await import.ListMappingProfilesAsync())
            .Single(p => p.Name == "Round Trip Supplier");

        reloaded.Name.Should().Be(saved.Name);
        reloaded.Mapping.Should().Be(saved.Mapping, "every mapped column must survive the JSON round trip exactly");
        reloaded.Should().Be(saved, "the whole profile - name and mapping together - must round-trip exactly");
    }

    private static ImportColumnMapping SupplierAMapping() => ImportColumnMapping.Default with
    {
        Code = "SKU",
        Name = "Description",
    };

    private static ImportColumnMapping SupplierBMapping() => ImportColumnMapping.Default with
    {
        Code = "Item No",
        Name = "Item Name",
        Barcode = "UPC",
    };
}
