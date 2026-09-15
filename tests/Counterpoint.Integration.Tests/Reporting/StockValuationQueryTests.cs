using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Reporting.Inventory;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The stock valuation report (task P2-T11 "Do this" #2), through the real SQLite database
/// <see cref="SaleFixture"/> composes.
/// </summary>
public sealed class StockValuationQueryTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task TheTotalTiesExactlyToSumOfStockBalanceQtyBaseTimesCostAvg()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        // A quantity and a cost that do not divide evenly at four decimal places once
        // multiplied - qty_base 3.3333 x cost_avg 3.3333 implies eight decimal places
        // (11.11088889). Money.Amount is not quantised until it is stored (Money's own remarks),
        // so the report is expected to carry all eight of them, not four - a tidy round number
        // would pass even if a rounding step had crept in by accident.
        var (variantId, _, pieceUomId) = await SeedProductWithVariantAsync(fixture, "VALUATION-ODD");
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, quantity: 3.3333m, unitCost: 3.3333m);

        var report = await fixture.Resolve<IStockValuationQuery>().GetValuationAsync();

        var rawTotalText = await fixture.ScalarAsync("SELECT SUM(qty_base * cost_avg) FROM stock_balance;");
        var rawTotal = long.Parse(rawTotalText!, CultureInfo.InvariantCulture);
        var expectedTotal = Money.FromDecimal(rawTotal / 100_000_000m);

        report.TotalValue.Should().Be(expectedTotal);

        // And the line for the seeded variant itself: qty x cost, independently rounded the
        // same half-away-from-zero way.
        var line = report.Lines.Should().Contain(l => l.ProductVariantId == variantId).Subject;
        line.QtyOnHandBase.Value.Should().Be(3.3333m);
        line.CostAvg.Should().Be(Money.FromDecimal(3.3333m));
        line.Value.Should().Be(Money.FromDecimal(11.11088889m));
    }

    [Fact]
    public async Task ACashierCannotOpenTheValuationReport()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.SignInAsSeededOwnerAsync();
        await fixture.Resolve<IUserAdministration>()
            .CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var valuation = fixture.Resolve<IStockValuationQuery>();

        var act = () => valuation.GetValuationAsync();

        await act.Should().ThrowAsync<NotAuthorisedException>();
        fixture.TryResolve<StockValuationQuery>().Should().BeNull(
            "the container must hand out only the role-decorated interface, never the concrete "
            + "reader - the same discipline UserAdministrationTests proves for IUserAdministration");
    }

    // ---- Shared seeding -----------------------------------------------------------------------

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture)
    {
        var uoms = await fixture.Resolve<IUomMaintenance>().ListAsync();
        foreach (var uom in uoms)
        {
            if (uom.Name == "Piece")
            {
                return uom.Id;
            }
        }

        throw new InvalidOperationException("The seeded catalogue has no 'Piece' unit.");
    }

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture)
    {
        var classes = await fixture.Resolve<ITaxClassMaintenance>().ListAsync();
        foreach (var taxClass in classes)
        {
            if (taxClass.Name == "Exempt")
            {
                return taxClass.Id;
            }
        }

        throw new InvalidOperationException("The seeded catalogue has no 'Exempt' tax class.");
    }

    private static async Task<(long VariantId, long ProductId, long PieceUomId)> SeedProductWithVariantAsync(
        SaleFixture fixture, string code)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Product " + code,
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            pieceUomId,
            ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null,
            ConfirmDuplicate: true));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(1.00m)));

        return (variantId, productId, pieceUomId);
    }

    private static async Task PostOpeningStockAsync(
        SaleFixture fixture, long variantId, long uomId, decimal quantity, decimal unitCost)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(quantity, uomId),
            Money.FromDecimal(unitCost),
            "OPENING",
            RefDocId: null,
            userId,
            PostedAt));
    }
}
