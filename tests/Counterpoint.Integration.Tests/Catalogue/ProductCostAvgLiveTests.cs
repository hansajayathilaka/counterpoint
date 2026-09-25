using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// <c>product.cost_avg</c> - the figure the below-cost pricing guard reads
/// (<c>ProductMaintenanceService.RequireAboveCostOrConfirmed</c>, SRS FR-2.18) - actually moves
/// with real stock receipts, rather than staying at the zero it is created with forever.
/// </summary>
/// <remarks>
/// Before this fix, nothing ever wrote to this column after <c>SqliteProductStore.CreateAsync</c>
/// seeded it to <see cref="Money.Zero"/> - only <c>stock_balance.cost_avg</c> (a different column,
/// keyed by variant) moved with GRNs, returns, adjustments and stock takes. Since every real price
/// is positive, <c>price &lt;= cost</c> could never be true against a cost of zero, so the guard
/// was permanently dead for every product created the ordinary way. <c>PriceChangeLogTests</c>
/// exercises the guard's own logic by setting <c>product.cost_avg</c> directly, which is exactly
/// why that gap went unnoticed - these tests instead drive the real inbound path
/// (<see cref="IStockLedger"/>) and prove the guard picks it up without any direct SQL.
/// </remarks>
public sealed class ProductCostAvgLiveTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 6, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AGoodsReceiptMovesProductCostAvgAndTheBelowCostGuardSeesItImmediately()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var (uomId, taxClassId) = await ReferenceDataAsync(fixture);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;

        var productId = await products.CreateAsync(new SaveProductCommand(
            "LIVE-COST-1", "Live cost product", null, null, null, uomId,
            ProductType.Standard, taxClassId, null, false, null, null, null));

        // No stock yet: cost is still zero, so any positive price is accepted without confirmation.
        var variantId = await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("LIVE-COST-1-A", CatalogueAttributes.Empty, Money.FromDecimal(1.00m)));

        (await fixture.ScalarAsync("SELECT cost_avg FROM product WHERE id = " + productId + ";"))
            .Should().Be("0", "nothing has been received yet");

        // A real goods receipt - the actual inbound door, not a raw SQL write - at 6.00 per unit.
        await fixture.Resolve<IStockLedger>().PostAsync(
            new StockPosting(
                variantId, "GRN", Quantity.FromDecimal(10m, uomId), Money.FromDecimal(6.00m),
                "GRN", RefDocId: null, userId, ReceivedAt),
            CancellationToken.None);

        (await fixture.ScalarAsync("SELECT cost_avg FROM product WHERE id = " + productId + ";"))
            .Should().Be(Money.FromDecimal(6.00m).ToScaled().ToString(System.Globalization.CultureInfo.InvariantCulture),
                "the receipt is the whole point of this column - it must actually reach product.cost_avg");

        // The guard now sees the real cost with no test-side SQL: a price at or below 6.00 warns.
        var atCost = () => products.UpdateVariantAsync(
            variantId, new SaveProductVariantCommand("LIVE-COST-1-A", CatalogueAttributes.Empty, Money.FromDecimal(6.00m)));
        await atCost.Should().ThrowAsync<PriceBelowCostWarningException>();

        var aboveCost = () => products.UpdateVariantAsync(
            variantId, new SaveProductVariantCommand("LIVE-COST-1-A", CatalogueAttributes.Empty, Money.FromDecimal(6.01m)));
        await aboveCost.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AnOutboundSaleMovementNeverTouchesProductCostAvg()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var (uomId, taxClassId) = await ReferenceDataAsync(fixture);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;

        var productId = await products.CreateAsync(new SaveProductCommand(
            "LIVE-COST-2", "Live cost product 2", null, null, null, uomId,
            ProductType.Standard, taxClassId, null, false, null, null, null));
        var variantId = await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("LIVE-COST-2-A", CatalogueAttributes.Empty, Money.FromDecimal(20.00m)));

        var ledger = fixture.Resolve<IStockLedger>();
        await ledger.PostAsync(
            new StockPosting(
                variantId, "GRN", Quantity.FromDecimal(10m, uomId), Money.FromDecimal(8.00m),
                "GRN", RefDocId: null, userId, ReceivedAt),
            CancellationToken.None);

        // An outbound (sale) movement never recomputes the average (StockLedgerMath's own rule) -
        // product.cost_avg must be left exactly as the receipt set it, not zeroed or disturbed.
        await ledger.PostAsync(
            new StockPosting(
                variantId, "SALE", Quantity.FromDecimal(-3m, uomId), Money.Zero,
                "SALE", RefDocId: null, userId, ReceivedAt.AddHours(1)),
            CancellationToken.None);

        (await fixture.ScalarAsync("SELECT cost_avg FROM product WHERE id = " + productId + ";"))
            .Should().Be(Money.FromDecimal(8.00m).ToScaled().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<(long UomId, long TaxClassId)> ReferenceDataAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<Counterpoint.Application.Catalogue.IUomMaintenance>();
        var taxClasses = fixture.Resolve<Counterpoint.Application.Catalogue.ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return (pieceId, exemptId);
    }

    private static class CatalogueAttributes
    {
        internal static readonly System.Collections.Generic.Dictionary<string, string> Empty = [];
    }
}
