using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// The stock enquiry screen's use case (F11, P1-T07, SRS FR-4).
/// </summary>
public sealed class StockEnquiryServiceTests
{
    [Fact]
    public async Task FR_4_StockEnquiryShowsQuantityInBaseAndAlternateUnitsAndHidesCostFromACashier()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var productId = await fixture.CountAsync("SELECT product_id FROM product_variant ORDER BY id LIMIT 1;");
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxUomId = await uoms.CreateAsync(new SaveUomCommand("Box of 10", "bx10", 0));

        var products = fixture.Resolve<IProductMaintenance>();
        await products.AddUomOptionAsync(
            productId,
            new SaveProductUomCommand(boxUomId, UomConversion.FromDecimal(10m), SellingPrice: null));

        var enquiry = fixture.Resolve<IStockEnquiry>();

        // The seeded owner session: cost is present.
        var ownerView = await enquiry.FindByVariantIdAsync(variantId);

        ownerView.Should().NotBeNull();
        ownerView!.QtyBase.Should().Be(100m, "the seeder opens with 100 pieces");
        ownerView.BaseUomSymbol.Should().Be("pc");
        ownerView.CostAvg.Should().Be(
            Money.FromDecimal(9.00m),
            "FirstRunSeeder receives the opening count at 9.00, and an owner session may see it");

        ownerView.AlternateUnits.Should().ContainSingle(unit => unit.UomId == boxUomId)
            .Which.Quantity.Should().Be(10m, "100 pieces at 10 per box is 10 boxes");

        ownerView.RecentMovements.Should().ContainSingle()
            .Which.UnitCost.Should().Be(Money.FromDecimal(9.00m));

        // A cashier session: the same quantities, but no cost anywhere in the result.
        var users = fixture.Resolve<IUserAdministration>();
        await users.CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var cashierView = await enquiry.FindByVariantIdAsync(variantId);

        cashierView.Should().NotBeNull();
        cashierView!.QtyBase.Should().Be(100m);
        cashierView.AlternateUnits.Should().ContainSingle(unit => unit.UomId == boxUomId)
            .Which.Quantity.Should().Be(10m);
        cashierView.CostAvg.Should().BeNull("cost is owner-only (CLAUDE.md invariant 8)");
        cashierView.RecentMovements.Should().OnlyContain(movement => movement.UnitCost == null);
    }

    [Fact]
    public async Task FR_4_AnUnknownVariantReturnsNull()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var result = await fixture.Resolve<IStockEnquiry>().FindByVariantIdAsync(999_999);

        result.Should().BeNull();
    }
}
