using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Purchasing;

/// <summary>
/// Suppliers and purchase orders (SRS FR-4.5, FR-4.6, FR-4.10, task P2-T06), through the real
/// SQLite database <see cref="SaleFixture"/> composes - the same container the application runs,
/// never the in-memory provider.
/// </summary>
public sealed class PurchaseOrderServiceTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_4_5_CreatingAPurchaseOrderAllocatesANumberAndWritesTheOrderAndItsLines()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var supplierId = await SeedSupplierAsync(fixture);
        var (variantId, boxUomId, pieceUomId) = await SeedBoxedNailsAsync(fixture);

        var command = new CreatePurchaseOrderCommand(
            supplierId,
            new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.FromHours(5.5)),
            "First order",
            [
                new CreatePurchaseOrderLineCommand(variantId, pieceUomId, 50m, Money.FromDecimal(5.00m)),
                new CreatePurchaseOrderLineCommand(variantId, boxUomId, 2m, Money.FromDecimal(450.00m)),
            ]);

        var created = await fixture.Resolve<IPurchaseOrderService>().CreateAsync(command);

        created.PoNo.Should().Be("PO-2026-000001");
        created.Status.Should().Be("DRAFT");
        created.SupplierId.Should().Be(supplierId);
        created.Lines.Should().HaveCount(2);
        created.Total.Should().Be(Money.FromDecimal((50m * 5.00m) + (2m * 450.00m)));

        (await fixture.ScalarAsync(
            "SELECT po_no || '|' || supplier_id || '|' || status FROM purchase_order WHERE po_no = 'PO-2026-000001';"))
            .Should().Be("PO-2026-000001|" + supplierId + "|DRAFT");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM purchase_order_line WHERE purchase_order_id = "
            + "(SELECT id FROM purchase_order WHERE po_no = 'PO-2026-000001');"))
            .Should().Be(2);

        (await fixture.ScalarAsync(
            "SELECT action || '|' || entity_type FROM audit_log WHERE action = 'PURCHASE_ORDER_CREATED' ORDER BY id DESC LIMIT 1;"))
            .Should().Be("PURCHASE_ORDER_CREATED|purchase_order");
    }

    [Fact]
    public async Task FR_4_5_CreatingAPurchaseOrderWithNoLinesIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var supplierId = await SeedSupplierAsync(fixture);

        var act = () => fixture.Resolve<IPurchaseOrderService>()
            .CreateAsync(new CreatePurchaseOrderCommand(supplierId, null, null, []));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.CountAsync("SELECT COUNT(*) FROM purchase_order;")).Should().Be(0);
    }

    [Fact]
    public async Task FR_4_5_ALineInAUnitTheProductDoesNotSellInIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var supplierId = await SeedSupplierAsync(fixture);
        var (variantId, _, _) = await SeedBoxedNailsAsync(fixture);

        // A unit that exists in the shop's UOM table but was never added to this product.
        var foreignUomId = await fixture.Resolve<IUomMaintenance>().CreateAsync(new SaveUomCommand("Litre", "L", 2));

        var act = () => fixture.Resolve<IPurchaseOrderService>().CreateAsync(new CreatePurchaseOrderCommand(
            supplierId, null, null, [new CreatePurchaseOrderLineCommand(variantId, foreignUomId, 5m, Money.FromDecimal(1.00m))]));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.CountAsync("SELECT COUNT(*) FROM purchase_order;")).Should().Be(0);
    }

    [Fact]
    public async Task FR_4_5_SendingADraftOrderMarksItSentAndSendingAgainIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);
        var service = fixture.Resolve<IPurchaseOrderService>();

        await service.SendAsync(orderId);

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + orderId + ";"))
            .Should().Be("SENT");

        var act = () => service.SendAsync(orderId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task P2_T06_CancellingAPurchaseOrderDoesNotAffectStock()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);

        var movementsBefore = await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement;");
        var balanceRowsBefore = await fixture.CountAsync("SELECT COUNT(*) FROM stock_balance;");

        await fixture.Resolve<IPurchaseOrderService>().CancelAsync(orderId, "Supplier could not fulfil");

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + orderId + ";"))
            .Should().Be("CANCELLED");

        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement;")).Should().Be(
            movementsBefore, "cancelling a purchase order must never post a stock movement (CLAUDE.md invariant 3)");
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_balance;")).Should().Be(balanceRowsBefore);

        (await fixture.ScalarAsync(
            "SELECT action FROM audit_log WHERE action = 'PURCHASE_ORDER_CANCELLED' ORDER BY id DESC LIMIT 1;"))
            .Should().Be("PURCHASE_ORDER_CANCELLED");
    }

    [Fact]
    public async Task FR_4_5_CancellingWithNoReasonIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);

        var act = () => fixture.Resolve<IPurchaseOrderService>().CancelAsync(orderId, "   ");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + orderId + ";"))
            .Should().Be("DRAFT");
    }

    [Fact]
    public async Task FR_4_5_CancellingAReceivedOrderIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);

        await fixture.ExecuteAsync("UPDATE purchase_order SET status = 'RECEIVED' WHERE id = " + orderId + ";");

        var act = () => fixture.Resolve<IPurchaseOrderService>().CancelAsync(orderId, "Too late");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FR_4_10_NoLineReceivedLeavesASentOrderSent()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);
        var service = fixture.Resolve<IPurchaseOrderService>();

        await service.SendAsync(orderId);
        await service.RecomputeStatusAsync(orderId);

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + orderId + ";"))
            .Should().Be("SENT");
    }

    [Fact]
    public async Task FR_4_10_PartiallyReceivingOneOfTwoLinesMarksTheOrderPartial()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantOneId, _, _) = await SeedBoxedNailsAsync(fixture, code: "NAIL-A");
        var (variantTwoId, _, _) = await SeedBoxedNailsAsync(fixture, code: "NAIL-B");

        var service = fixture.Resolve<IPurchaseOrderService>();
        var created = await service.CreateAsync(new CreatePurchaseOrderCommand(
            supplierId,
            null,
            null,
            [
                new CreatePurchaseOrderLineCommand(variantOneId, pieceUomId, 100m, Money.FromDecimal(1.00m)),
                new CreatePurchaseOrderLineCommand(variantTwoId, pieceUomId, 100m, Money.FromDecimal(1.00m)),
            ]));

        await service.SendAsync(created.Id);

        // GRN posting is P2-T07's - this reaches the same post-GRN state (some quantity received
        // against one line only) the way OpenShiftHandlerTests reaches "no shift open": a raw
        // repair-session UPDATE to qty_received_base, the one column a goods receipt would have
        // written, without needing the GRN command that does not exist yet.
        var firstLineId = created.Lines[0].Id;
        await fixture.ExecuteAsync(
            "UPDATE purchase_order_line SET qty_received_base = " + Quantity.FromDecimal(40m, pieceUomId).ToScaled()
            + " WHERE id = " + firstLineId + ";");

        await service.RecomputeStatusAsync(created.Id);

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + created.Id + ";"))
            .Should().Be("PARTIAL");
    }

    [Fact]
    public async Task FR_4_10_EveryLineFullyReceivedMarksTheOrderReceived()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedBoxedNailsAsync(fixture);

        var service = fixture.Resolve<IPurchaseOrderService>();
        var created = await service.CreateAsync(new CreatePurchaseOrderCommand(
            supplierId, null, null,
            [new CreatePurchaseOrderLineCommand(variantId, pieceUomId, 100m, Money.FromDecimal(1.00m))]));

        await service.SendAsync(created.Id);

        await fixture.ExecuteAsync(
            "UPDATE purchase_order_line SET qty_received_base = " + Quantity.FromDecimal(100m, pieceUomId).ToScaled()
            + " WHERE purchase_order_id = " + created.Id + ";");

        await service.RecomputeStatusAsync(created.Id);

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + created.Id + ";"))
            .Should().Be("RECEIVED");
    }

    [Fact]
    public async Task FR_4_5_PrintingAPurchaseOrderQueuesAPdfPrintJob()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);

        var jobsBefore = await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_type = 'PO';");

        var printJobId = await fixture.Resolve<IPurchaseOrderService>().PrintAsync(orderId);

        printJobId.Should().BeGreaterThan(0);

        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_type = 'PO';"))
            .Should().Be(jobsBefore + 1);

        (await fixture.ScalarAsync(
            "SELECT doc_id || '|' || status FROM print_job WHERE id = " + printJobId + ";"))
            .Should().Be(orderId + "|PENDING");

        (await fixture.ScalarAsync(
            "SELECT action FROM audit_log WHERE action = 'PURCHASE_ORDER_PRINTED' ORDER BY id DESC LIMIT 1;"))
            .Should().Be("PURCHASE_ORDER_PRINTED");
    }

    [Fact]
    public async Task FR_4_10_RecomputingADraftOrderDoesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var orderId = await CreateSimpleOrderAsync(fixture);

        await fixture.Resolve<IPurchaseOrderService>().RecomputeStatusAsync(orderId);

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + orderId + ";"))
            .Should().Be("DRAFT", "receipt progress never drives DRAFT or CANCELLED - only an explicit Send does");
    }

    [Fact]
    public async Task FR_4_6_SuggestedOrderProposesTheRightItemAndQuantityFromReorderLevels()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (belowVariantId, belowProductId, pieceUomId) = await SeedProductWithVariantAsync(fixture, "LOW-STOCK-1");
        var (aboveVariantId, aboveProductId, _) = await SeedProductWithVariantAsync(fixture, "HEALTHY-STOCK-1");

        // Below its reorder level of 20 (only 5 on hand) - proposes reorder_qty of 50.
        await fixture.ExecuteAsync(
            "UPDATE product SET reorder_level = " + Quantity.FromDecimal(20m, pieceUomId).ToScaled()
            + ", reorder_qty = " + Quantity.FromDecimal(50m, pieceUomId).ToScaled()
            + " WHERE id = " + belowProductId + ";");
        await PostOpeningStockAsync(fixture, belowVariantId, pieceUomId, 5m);

        // Comfortably above its own reorder level - must not appear.
        await fixture.ExecuteAsync(
            "UPDATE product SET reorder_level = " + Quantity.FromDecimal(20m, pieceUomId).ToScaled()
            + ", reorder_qty = " + Quantity.FromDecimal(50m, pieceUomId).ToScaled()
            + " WHERE id = " + aboveProductId + ";");
        await PostOpeningStockAsync(fixture, aboveVariantId, pieceUomId, 500m);

        var suggested = await fixture.Resolve<IPurchaseOrderService>().GetSuggestedOrderAsync();

        var lowStockLine = suggested.Should().ContainSingle(line => line.ProductId == belowProductId).Subject;
        lowStockLine.QtyOnHandBase.Value.Should().Be(5m);
        lowStockLine.ReorderLevel.Value.Should().Be(20m);
        lowStockLine.SuggestedQty.Value.Should().Be(50m);

        suggested.Should().NotContain(line => line.ProductId == aboveProductId);
    }

    [Fact]
    public async Task AC_17_ACashierIsRefusedByThePurchaseOrderServiceItself()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await fixture.SignInAsSeededOwnerAsync();
        await fixture.Resolve<IUserAdministration>()
            .CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var purchaseOrders = fixture.Resolve<IPurchaseOrderService>();

        var list = () => purchaseOrders.ListAsync();
        var create = () => purchaseOrders.CreateAsync(new CreatePurchaseOrderCommand(1, null, null, []));

        await list.Should().ThrowAsync<NotAuthorisedException>();
        await create.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM purchase_order;")).Should().Be(0);
        fixture.TryResolve<PurchaseOrderService>().Should().BeNull(
            "the container must hand out only the role-decorated interface, never the concrete service");
    }

    // ---- Shared seeding -----------------------------------------------------------------------

    private static async Task<long> CreateSimpleOrderAsync(SaleFixture fixture)
    {
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedBoxedNailsAsync(fixture);

        var created = await fixture.Resolve<IPurchaseOrderService>().CreateAsync(new CreatePurchaseOrderCommand(
            supplierId,
            null,
            null,
            [new CreatePurchaseOrderLineCommand(variantId, pieceUomId, 10m, Money.FromDecimal(2.00m))]));

        return created.Id;
    }

    private static async Task<long> SeedSupplierAsync(SaleFixture fixture, string name = "Ceylon Hardware Suppliers")
    {
        // The real till always has this row before a purchase order can exist -
        // FirstRunSetupService.ConfigureNumberingAsync creates every FR-10.4 series, PO included,
        // during the wizard the shop actually runs (task P2-T06's own context note). SaleFixture
        // seeds only SALE and SHIFT (FirstRunSeeder, the P0-T06 walking-skeleton seeder) to keep
        // every other test's setup cheap, so a purchase-order test creates its own PO series here
        // the same way ISettings.SaveAsync/FirstRunSetupService would - through the same public
        // port, never by inserting the row by hand.
        await fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("PO", "PO-", "{prefix}{yyyy}-{n:000000}", 1);

        return await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand(name, null, null, null, null, null));
    }

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

    /// <summary>A boxed product: nails, sellable in the base piece unit or a box of 100 - the same shape <c>CompleteSaleTenderTests.SeedBoxedNailsAsync</c> seeds for the sale path.</summary>
    private static async Task<(long VariantId, long BoxUomId, long PieceUomId)> SeedBoxedNailsAsync(
        SaleFixture fixture, string code = "NAIL-BOXED")
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var pieceUomId = await PieceUomIdAsync(fixture);
        var taxClassId = await ExemptTaxClassIdAsync(fixture);

        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box of 100 (" + code + ")", "box", 0));

        var productId = await products.CreateAsync(new SaveProductCommand(
            code,
            "Boxed nails " + code,
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
            new SaveProductVariantCommand(code + "-A", new System.Collections.Generic.Dictionary<string, string>(), Money.FromDecimal(0.10m)));

        await products.AddUomOptionAsync(
            productId,
            new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), SellingPrice: null));

        return (variantId, boxId, pieceUomId);
    }

    /// <summary>A plain single-unit product, for the suggested-order report's own reorder-level comparison.</summary>
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
            new SaveProductVariantCommand(code + "-A", new System.Collections.Generic.Dictionary<string, string>(), Money.FromDecimal(1.00m)));

        return (variantId, productId, pieceUomId);
    }

    private static async Task PostOpeningStockAsync(SaleFixture fixture, long variantId, long uomId, decimal quantity)
    {
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "OPENING",
            Quantity.FromDecimal(quantity, uomId),
            Money.FromDecimal(1.00m),
            "OPENING",
            RefDocId: null,
            userId,
            SoldAt));
    }
}
