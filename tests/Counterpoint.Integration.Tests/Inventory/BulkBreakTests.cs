using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Inventory;

/// <summary>
/// Bulk breaking (SRS FR-4.9, AC-09, task P2-T09): a balanced pair moving value between two
/// different <c>product_variant</c> rows, wastage handled explicitly, and a value-conservation
/// report that finds nothing unbalanced. Runs against the real SQLite file <see cref="SaleFixture"/>
/// composes, never the in-memory provider.
/// </summary>
public sealed class BulkBreakTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 9, 14, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task AC_09_ACoilToLooseMetreBreakPostsABalancedPairWithCostCarriedAcross()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (sourceId, destinationId) = await SeedSourceAndDestinationAsync(fixture, "AC09");

        // 1 coil already on the shelf at Rs 9000.0000.
        await PostOpeningStockAsync(fixture, sourceId, 1m, 9000.00m);

        var result = await fixture.Resolve<IPostBulkBreak>().PostAsync(new BulkBreakCommand(
            sourceId, 1m, destinationId, 90m, 90m, "Break coil into loose metres", PostedAt));

        // AC-09: no wastage, so the whole coil's value carries across, per metre, exactly.
        result.WastageQtyBase.IsZero.Should().BeTrue();
        result.TotalValue.Should().Be(Money.FromDecimal(9000.00m));
        result.DestinationUnitCost.Should().Be(Money.FromDecimal(100.00m), "9000 / 90 = 100.0000 exactly");

        var outMovement = await fixture.ScalarAsync(
            "SELECT movement_type || '|' || qty_base || '|' || unit_cost || '|' || ref_doc_type || '|' || ref_doc_id "
            + "FROM stock_movement WHERE product_variant_id = " + sourceId + " AND movement_type = 'BULK_BREAK_OUT';");
        outMovement.Should().Be(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"BULK_BREAK_OUT|-10000|90000000|BULK_BREAK|{result.BulkBreakId}"));

        var inMovement = await fixture.ScalarAsync(
            "SELECT movement_type || '|' || qty_base || '|' || unit_cost || '|' || ref_doc_type || '|' || ref_doc_id "
            + "FROM stock_movement WHERE product_variant_id = " + destinationId + " AND movement_type = 'BULK_BREAK_IN';");
        inMovement.Should().Be(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"BULK_BREAK_IN|900000|1000000|BULK_BREAK|{result.BulkBreakId}"));

        // The destination's moving-average cost after the break matches the hand-worked figure.
        (await fixture.ScalarAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + destinationId + ";"))
            .Should().Be("1000000");
        (await fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + destinationId + ";"))
            .Should().Be("900000");

        // No DAMAGE row when nothing was wasted.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'BULK_BREAK' AND ref_doc_id = "
            + result.BulkBreakId + " AND movement_type = 'DAMAGE';")).Should().Be(0);

        var unbalanced = await fixture.Resolve<IBulkBreakValueConservationQuery>().FindUnbalancedAsync();
        unbalanced.Should().BeEmpty("a balanced pair with cost carried across must conserve value exactly");
    }

    [Fact]
    public async Task FR_4_9_DeclaredWastagePostsAsDamageAgainstTheSourceAndTheThreeMovementsConserveValue()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (sourceId, destinationId) = await SeedSourceAndDestinationAsync(fixture, "WASTE");

        // 1 coil at Rs 9000.0000, expecting 100m but only 90m actually came out - chosen so every
        // division involved (the wastage ratio, and the destination's absorbed unit cost) divides
        // exactly, the same "hand-worked, not just observed" standard as AC-09 above. An
        // awkward-fraction ratio (say 88 out of 90) still conserves to within a sub-scaled-unit
        // residual - see PostBulkBreakHandler's own remarks on why a fixed 4dp qty x cost
        // representation cannot always reconstruct an arbitrary target bit-for-bit, the same
        // class of imprecision GoodsReceiptService's own unitCostBase division already carries.
        await PostOpeningStockAsync(fixture, sourceId, 1m, 9000.00m);

        var result = await fixture.Resolve<IPostBulkBreak>().PostAsync(new BulkBreakCommand(
            sourceId, 1m, destinationId, 100m, 90m, "Break coil, some cable was unusable", PostedAt));

        result.WastageQtyBase.Value.Should().Be(10m);
        result.WastageValue.Should().NotBeNull();
        result.WastageValue!.Value.IsPositive.Should().BeTrue();

        // The wastage write-off targets the SOURCE, not the destination - see
        // PostBulkBreakHandler's own remarks for why. It is a genuine stock decrease (a negative
        // movement), never a positive one.
        var damage = await fixture.ScalarAsync(
            "SELECT product_variant_id || '|' || qty_base || '|' || ref_doc_type || '|' || ref_doc_id "
            + "FROM stock_movement WHERE ref_doc_type = 'BULK_BREAK' AND ref_doc_id = " + result.BulkBreakId
            + " AND movement_type = 'DAMAGE';");
        damage.Should().NotBeNull();
        var damageParts = damage!.Split('|');
        damageParts[0].Should().Be(sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        long.Parse(damageParts[1], System.Globalization.CultureInfo.InvariantCulture).Should().BeLessThan(0);

        // Exactly three movements share this break's ref_doc_id: OUT, IN, DAMAGE.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'BULK_BREAK' AND ref_doc_id = " + result.BulkBreakId + ";"))
            .Should().Be(3);

        // The group's own three movements sum to zero - this is what the value-conservation
        // report checks, and what task P2-T09's own "Done when" requires.
        var netValueRaw = await fixture.ScalarAsync(
            "SELECT SUM(qty_base * unit_cost) FROM stock_movement WHERE ref_doc_type = 'BULK_BREAK' AND ref_doc_id = "
            + result.BulkBreakId + ";");
        netValueRaw.Should().Be("0", "the OUT, IN and DAMAGE movements together must conserve value exactly");

        var unbalanced = await fixture.Resolve<IBulkBreakValueConservationQuery>().FindUnbalancedAsync();
        unbalanced.Should().BeEmpty();
    }

    [Fact]
    public async Task AC_17_ACashierCannotPostABulkBreakButAnOwnerCan()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (sourceId, destinationId) = await SeedSourceAndDestinationAsync(fixture, "AUTH");
        await PostOpeningStockAsync(fixture, sourceId, 1m, 9000.00m);

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var attempt = async () => await fixture.Resolve<IPostBulkBreak>().PostAsync(
            new BulkBreakCommand(sourceId, 1m, destinationId, 90m, 90m, "Cashier trying to break stock", PostedAt));

        await attempt.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM bulk_break;")).Should().Be(0);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type IN ('BULK_BREAK_OUT','BULK_BREAK_IN');"))
            .Should().Be(0);

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword))
            .Succeeded.Should().BeTrue();

        var result = await fixture.Resolve<IPostBulkBreak>().PostAsync(
            new BulkBreakCommand(sourceId, 1m, destinationId, 90m, 90m, "Owner breaking stock", PostedAt));

        result.ActualQtyBase.Value.Should().Be(90m);
        (await fixture.CountAsync("SELECT COUNT(*) FROM bulk_break;")).Should().Be(1);
    }

    private static async Task<(long SourceId, long DestinationId)> SeedSourceAndDestinationAsync(
        SaleFixture fixture, string code)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var sourceProductId = await products.CreateAsync(new SaveProductCommand(
            code + "-COIL",
            "Coil " + code,
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
        var sourceVariantId = await products.CreateVariantAsync(
            sourceProductId, new SaveProductVariantCommand(code + "-COIL-A", EmptyAttributes, Money.FromDecimal(9500.00m)));

        var destinationProductId = await products.CreateAsync(new SaveProductCommand(
            code + "-METRE",
            "Loose cable " + code,
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
        var destinationVariantId = await products.CreateVariantAsync(
            destinationProductId, new SaveProductVariantCommand(code + "-METRE-A", EmptyAttributes, Money.FromDecimal(150.00m)));

        return (sourceVariantId, destinationVariantId);
    }

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

    private static async Task PostOpeningStockAsync(SaleFixture fixture, long variantId, decimal quantity, decimal unitCost)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var pieceUomId = await PieceUomIdAsync(fixture);

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(quantity, pieceUomId),
            Money.FromDecimal(unitCost),
            "OPENING",
            RefDocId: null,
            userId,
            PostedAt));
    }
}
