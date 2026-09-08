using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// Every catalogue reference-data service is owner only, refused by the Application layer
/// itself with no screen involved (SRS NFR-S2, AC-17) - the same shape
/// <c>UserAdministrationTests.AC_17_ACashierIsRefusedByTheServiceItselfNotByAHiddenButton</c>
/// proves for user management.
/// </summary>
public sealed class CatalogueAuthorisationTests
{
    [Fact]
    public async Task AC_17_ACashierIsRefusedByEveryCatalogueMaintenanceServiceItself()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.SignInAsSeededOwnerAsync();
        var users = fixture.Resolve<IUserAdministration>();
        await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var categories = fixture.Resolve<ICategoryMaintenance>();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var suppliers = fixture.Resolve<ISupplierMaintenance>();
        var customers = fixture.Resolve<ICustomerMaintenance>();

        var listCategory = () => categories.ListAsync();
        var createCategory = () => categories.CreateAsync(new SaveCategoryCommand("Mallory", null));
        var createBrand = () => brands.CreateAsync(new SaveBrandCommand("Mallory"));
        var createUom = () => uoms.CreateAsync(new SaveUomCommand("Mallory", "mal", 0));
        var createTaxClass = () => taxClasses.CreateAsync(new SaveTaxClassCommand("Mallory", TaxRate.Zero));
        var createSupplier = () => suppliers.CreateAsync(new SaveSupplierCommand("Mallory", null, null, null, null, null));
        var createCustomer = () =>
            customers.CreateAsync(new SaveCustomerCommand("Mallory", null, null, null, "RETAIL", Money.Zero));

        await listCategory.Should().ThrowAsync<NotAuthorisedException>();
        await createCategory.Should().ThrowAsync<NotAuthorisedException>();
        await createBrand.Should().ThrowAsync<NotAuthorisedException>();
        await createUom.Should().ThrowAsync<NotAuthorisedException>();
        await createTaxClass.Should().ThrowAsync<NotAuthorisedException>();
        await createSupplier.Should().ThrowAsync<NotAuthorisedException>();
        await createCustomer.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM category WHERE name = 'Mallory';"))
            .Should().Be(0, "nothing ran, so nothing was written");
        (await fixture.CountAsync("SELECT COUNT(*) FROM brand WHERE name = 'Mallory';")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM uom WHERE name = 'Mallory';")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM tax_class WHERE name = 'Mallory';")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM supplier WHERE name = 'Mallory';")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM customer WHERE name = 'Mallory';")).Should().Be(0);
    }

    [Fact]
    public async Task AC_17_TheContainerHasNoUndecoratedCatalogueServiceToHandOut()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        fixture.TryResolve<CategoryMaintenanceService>().Should().BeNull();
        fixture.TryResolve<BrandMaintenanceService>().Should().BeNull();
        fixture.TryResolve<UomMaintenanceService>().Should().BeNull();
        fixture.TryResolve<TaxClassMaintenanceService>().Should().BeNull();
        fixture.TryResolve<SupplierMaintenanceService>().Should().BeNull();
        fixture.TryResolve<CustomerMaintenanceService>().Should().BeNull();

        fixture.Resolve<ICategoryMaintenance>().Should().NotBeOfType<CategoryMaintenanceService>();
    }
}
