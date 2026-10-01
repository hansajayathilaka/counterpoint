using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Reporting;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The id-and-name lists behind the category, brand and supplier filters of the P3-T06 screens: active rows only, by
/// name, a child category shown under its parent, nothing else exposed. A plain registration (names are not cost
/// bearing), so a cashier's stock-on-hand and reorder screens can fill their filters too.
/// </summary>
public sealed class ReportFilterLookupTests
{
    [Fact]
    public async Task RPT_08_TheFilterListsHoldActiveRowsByNameWithAChildCategoryShownUnderItsParent()
    {
        await using var host = await SaleFixture.CreateSignedInAsync();

        var categories = host.Resolve<ICategoryMaintenance>();
        var fasteners = await categories.CreateAsync(new SaveCategoryCommand("Fasteners", null));
        var screws = await categories.CreateAsync(new SaveCategoryCommand("Machine screws", fasteners));
        var retired = await categories.CreateAsync(new SaveCategoryCommand("Retired range", null));
        await categories.DeactivateAsync(retired);

        var brands = host.Resolve<IBrandMaintenance>();
        await brands.CreateAsync(new SaveBrandCommand("Makita"));
        var oldBrand = await brands.CreateAsync(new SaveBrandCommand("Bosch"));
        await brands.CreateAsync(new SaveBrandCommand("Acme"));
        await brands.DeactivateAsync(oldBrand);

        var suppliers = host.Resolve<ISupplierMaintenance>();
        await suppliers.CreateAsync(new SaveSupplierCommand("Zenith Tools", null, null, null, null, null));
        await suppliers.CreateAsync(new SaveSupplierCommand("Acme Fasteners", null, null, null, null, null));
        var closed = await suppliers.CreateAsync(new SaveSupplierCommand("Closed Ltd", null, null, null, null, null));
        await suppliers.DeactivateAsync(closed);

        var lookup = host.Resolve<IReportFilterLookup>();

        var categoryOptions = await lookup.ListCategoriesAsync();
        categoryOptions.Select(option => option.Name).Should().Equal("Fasteners", "Fasteners / Machine screws");
        categoryOptions.Select(option => option.Id).Should().Equal(fasteners, screws);

        (await lookup.ListBrandsAsync()).Select(option => option.Name).Should().Equal("Acme", "Makita");
        (await lookup.ListSuppliersAsync()).Select(option => option.Name).Should().Equal("Acme Fasteners", "Zenith Tools");
    }
}
