using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Security;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Purchasing;

/// <summary>
/// Goods receipt (SRS FR-4.7, FR-4.8, AC-08, task P2-T07), through the real SQLite database
/// <see cref="SaleFixture"/> composes - the same container the application runs, never the
/// in-memory provider.
/// </summary>
public sealed class GoodsReceiptServiceTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly Dictionary<string, string> EmptyAttributes = [];

    [Fact]
    public async Task AC_08_ABoxToPieceGoodsReceiptIncreasesStockInBaseUnitsAndUpdatesMovingAverageCostCorrectly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var (variantId, boxUomId, pieceUomId) = await SeedBoxedNailsAsync(fixture);

        // 50 pieces already on the shelf at Rs 2.00 each.
        await PostOpeningStockAsync(fixture, variantId, pieceUomId, 50m, 2.00m);

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId,
            PurchaseOrderId: null,
            SupplierInvoiceNo: "INV-0001",
            ReceivedAt,
            OtherCost: Money.Zero,
            Note: null,
            [new CreateGoodsReceiptLineCommand(variantId, boxUomId, 2m, Money.FromDecimal(450.00m))]));

        // AC-08: 2 boxes of 100 land as 200 pieces in the base unit, not 2.
        result.Receipt.Lines.Should().ContainSingle();
        var line = result.Receipt.Lines[0];
        line.QtyBase.Value.Should().Be(200m);
        line.QtyBase.UomId.Should().Be(pieceUomId);

        // This line's own landed cost per base unit: Rs 450/box over 100 pieces/box, no freight.
        line.UnitCostBase.Should().Be(Money.FromDecimal(4.50m));

        // The moving average blends that into what was already on the shelf:
        // (50 x 2.00 + 200 x 4.50) / 250 = 1000 / 250 = 4.00 exactly.

        (await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(Quantity.FromDecimal(250m, pieceUomId).ToScaled().ToString());

        (await fixture.ScalarAsync(
            "SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(Money.FromDecimal(4.00m).ToScaled().ToString());

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'GRN' AND ref_doc_id = " + result.Receipt.Id + ";"))
            .Should().Be(1);
    }

    [Fact]
    public async Task FR_4_7_FreightIsApportionedAcrossLinesAndSumsToExactlyTheEnteredAmount()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);

        // Three lines, each a 7-piece subtotal of Rs 7.00 (21.00 total) - Rs 10.00 freight split
        // three ways does not divide evenly (3.3333... each).
        var (variantOne, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-FR-A");
        var (variantTwo, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-FR-B");
        var (variantThree, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-FR-C");

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId,
            null,
            null,
            ReceivedAt,
            OtherCost: Money.FromDecimal(10.00m),
            Note: null,
            [
                new CreateGoodsReceiptLineCommand(variantOne, pieceUomId, 7m, Money.FromDecimal(1.00m)),
                new CreateGoodsReceiptLineCommand(variantTwo, pieceUomId, 7m, Money.FromDecimal(1.00m)),
                new CreateGoodsReceiptLineCommand(variantThree, pieceUomId, 7m, Money.FromDecimal(1.00m)),
            ]));

        result.Receipt.OtherCost.Should().Be(Money.FromDecimal(10.00m));
        result.Receipt.Total.Should().Be(Money.FromDecimal(21.00m + 10.00m));

        var lineTotalSumScaled = result.Receipt.Lines.Sum(l => l.LineTotal.ToScaled());
        lineTotalSumScaled.Should().Be(result.Receipt.Total.ToScaled(),
            "every line's own total must sum to exactly the header total, freight included, to the ten-thousandth");

        (await fixture.ScalarAsync(
            "SELECT subtotal + tax + other_cost FROM goods_receipt WHERE id = " + result.Receipt.Id + ";"))
            .Should().Be(result.Receipt.Total.ToScaled().ToString());
    }

    [Fact]
    public async Task P2_T07_ATinyFractionalSubtotalAndTaxNoLongerThrowsAndRoundsToZero()
    {
        // Regression for the code-review defect: a line whose Subtotal (UnitCost x Quantity) and
        // whose Tax both carry more precision than Money's own storage scale used to make
        // RequireReceiptBalances throw a false "must match exactly" - subtotal and tax were each
        // independently quantised to the ten-thousandth (0.00005 -> 0.0001) while the line total
        // was built from the same two raw, unrounded values (0.00005 + 0.00005 -> 0.0001, itself
        // quantised to 0.0001), leaving 0.0001 != 0.0002. Rounding both to the currency's own
        // decimal places (Rs 0.00, SettingDefaults.Financial) before either is summed - the same
        // "round once, sum the already-rounded values" shape CompleteSaleHandler uses - closes
        // that off: everything here is well below half a cent, so both round down to zero, and a
        // receipt that used to be refused now posts, exactly.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-ROUND-A");

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var act = () => grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, OtherCost: Money.Zero, Note: null,
            [new CreateGoodsReceiptLineCommand(
                variantId, pieceUomId, 1m, Money.FromDecimal(0.00005m), Money.FromDecimal(0.00005m))]));

        var result = await act.Should().NotThrowAsync(
            "a legitimate receipt must never be refused for a false rounding mismatch");

        result.Subject.Receipt.Subtotal.Should().Be(Money.Zero);
        result.Subject.Receipt.Tax.Should().Be(Money.Zero);
        result.Subject.Receipt.OtherCost.Should().Be(Money.Zero);
        result.Subject.Receipt.Total.Should().Be(Money.Zero);

        var line = result.Subject.Receipt.Lines.Should().ContainSingle().Subject;
        line.Tax.Should().Be(Money.Zero);
        line.LineTotal.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task P2_T07_FreightTaxAndAFractionalSubtotalReconcileExactlyAfterRounding()
    {
        // The fuller real-world shape: a fractional Subtotal (a hardware shop sells cable and
        // wire below whole-currency precision), a freight share and a manually entered Tax, all
        // on the same line. Subtotal (0.335) and Tax (0.125) both sit exactly on the currency's
        // own rounding boundary (Rs 0.01, half away from zero) and round up; the freight share is
        // already exact by construction (GoodsReceiptFreightApportioner). The receipt must still
        // balance to the ten-thousandth, and the AC-08 stock-ledger cost path must keep using the
        // raw, unrounded Subtotal - never the rounded receipt figure.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-ROUND-B", sellingPrice: 10.00m);

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, OtherCost: Money.FromDecimal(1.00m), Note: null,
            [new CreateGoodsReceiptLineCommand(
                variantId, pieceUomId, 1m, Money.FromDecimal(0.335m), Money.FromDecimal(0.125m))]));

        // Rounded once, at the line-total point: 0.335 -> 0.34, 0.125 -> 0.13 (both half away
        // from zero, Rs 0.01). The header is nothing but their sum - no second, differently
        // ordered recombination for RequireReceiptBalances to disagree with.
        result.Receipt.Subtotal.Should().Be(Money.FromDecimal(0.34m));
        result.Receipt.Tax.Should().Be(Money.FromDecimal(0.13m));
        result.Receipt.OtherCost.Should().Be(Money.FromDecimal(1.00m));
        result.Receipt.Total.Should().Be(Money.FromDecimal(1.47m));

        var line = result.Receipt.Lines.Should().ContainSingle().Subject;
        line.Tax.Should().Be(Money.FromDecimal(0.13m));
        line.LineTotal.Should().Be(Money.FromDecimal(1.47m));

        // The stock-ledger cost path is untouched: landed cost is the raw 0.335 subtotal plus the
        // Rs 1.00 freight share, not the rounded 0.34 - full precision, exactly as AC-08 needs.
        line.UnitCostBase.Should().Be(Money.FromDecimal(1.335m));

        (await fixture.ScalarAsync(
            "SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(Money.FromDecimal(1.335m).ToScaled().ToString());
    }

    [Fact]
    public async Task FR_4_10_ReceivingAgainstAPurchaseOrderUpdatesQtyReceivedBaseAndAdvancesStatus()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        await fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("PO", "PO-", "{prefix}{yyyy}-{n:000000}", 1);

        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-PO-A");

        var purchaseOrders = fixture.Resolve<IPurchaseOrderService>();
        var order = await purchaseOrders.CreateAsync(new CreatePurchaseOrderCommand(
            supplierId, null, null,
            [new CreatePurchaseOrderLineCommand(variantId, pieceUomId, 100m, Money.FromDecimal(1.00m))]));
        await purchaseOrders.SendAsync(order.Id);

        var grn = fixture.Resolve<IGoodsReceiptService>();

        await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, order.Id, null, ReceivedAt, Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 40m, Money.FromDecimal(1.00m))]));

        (await fixture.ScalarAsync(
            "SELECT qty_received_base FROM purchase_order_line WHERE purchase_order_id = " + order.Id + ";"))
            .Should().Be(Quantity.FromDecimal(40m, pieceUomId).ToScaled().ToString());

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + order.Id + ";"))
            .Should().Be("PARTIAL");

        await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, order.Id, null, ReceivedAt, Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 60m, Money.FromDecimal(1.00m))]));

        (await fixture.ScalarAsync(
            "SELECT qty_received_base FROM purchase_order_line WHERE purchase_order_id = " + order.Id + ";"))
            .Should().Be(Quantity.FromDecimal(100m, pieceUomId).ToScaled().ToString());

        (await fixture.ScalarAsync("SELECT status FROM purchase_order WHERE id = " + order.Id + ";"))
            .Should().Be("RECEIVED");
    }

    [Fact]
    public async Task FR_4_5_ReceivingAgainstACancelledPurchaseOrderIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        await fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("PO", "PO-", "{prefix}{yyyy}-{n:000000}", 1);

        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-CANCEL-A");

        var purchaseOrders = fixture.Resolve<IPurchaseOrderService>();
        var order = await purchaseOrders.CreateAsync(new CreatePurchaseOrderCommand(
            supplierId, null, null,
            [new CreatePurchaseOrderLineCommand(variantId, pieceUomId, 10m, Money.FromDecimal(1.00m))]));
        await purchaseOrders.CancelAsync(order.Id, "Supplier out of stock");

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var act = () => grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, order.Id, null, ReceivedAt, Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 10m, Money.FromDecimal(1.00m))]));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.CountAsync("SELECT COUNT(*) FROM goods_receipt;")).Should().Be(0);
    }

    [Fact]
    public async Task FR_4_8_AGoodsReceiptWhoseCostExceedsTheSellingPriceFlagsAPriceReview()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);

        // Sells for Rs 1.00 today.
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-PRICE-A", sellingPrice: 1.00m);

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, Money.Zero, null,

            // Costs Rs 5.00 on this receipt - well above the Rs 1.00 it sells for.
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 10m, Money.FromDecimal(5.00m))]));

        var flag = result.PriceReviewFlags.Should().ContainSingle().Subject;
        flag.ProductVariantId.Should().Be(variantId);
        flag.LandedUnitCostBase.Should().Be(Money.FromDecimal(5.00m));
        flag.CurrentSellingPrice.Should().Be(Money.FromDecimal(1.00m));
    }

    [Fact]
    public async Task FR_4_8_AGoodsReceiptAtOrBelowTheSellingPriceFlagsNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-PRICE-B", sellingPrice: 10.00m);

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 10m, Money.FromDecimal(5.00m))]));

        result.PriceReviewFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task P2_T07_TheSuppliersLastCostIsRecordedExcludingThisShipmentsFreight()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, productId, _) = await SeedProductWithVariantAsync(fixture, "GRN-LASTCOST-A");

        var grn = fixture.Resolve<IGoodsReceiptService>();

        // Rs 2.00/piece from the supplier, plus Rs 20.00 freight on 10 pieces (Rs 2.00/piece
        // landed extra) - last_cost must stay the plain Rs 2.00 supplier price, not Rs 4.00.
        await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, Money.FromDecimal(20.00m), null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 10m, Money.FromDecimal(2.00m))]));

        (await fixture.ScalarAsync(
            "SELECT last_cost FROM product_supplier WHERE product_id = " + productId + " AND supplier_id = " + supplierId + ";"))
            .Should().Be(Money.FromDecimal(2.00m).ToScaled().ToString());
    }

    [Fact]
    public async Task FR_2_12_LabelsForTheReceivedBatchPrintCorrectly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-LABEL-A");

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 5m, Money.FromDecimal(1.00m))]));

        result.LabelPrintOutcome.Succeeded.Should().BeTrue(result.LabelPrintOutcome.FailureReason ?? string.Empty);
        Directory.Exists(fixture.LabelDirectory).Should().BeTrue("the received batch must have reached the label printer");
        Directory.GetFiles(fixture.LabelDirectory, "*.bin").Should().NotBeEmpty();
    }

    [Fact]
    public async Task FR_2_12_ALabelPrinterFailureDoesNotThrowAndDoesNotRollBackTheAlreadyCommittedReceipt()
    {
        // CLAUDE.md invariant 7 ("never block the sale") applied to a GRN: the label batch is
        // printed after ReceiveAsync's own transaction has already committed (the class remarks
        // on GoodsReceiptService.ReceiveAsync say so explicitly), so a broken label printer must
        // never throw out of ReceiveAsync and must never take the stock posting or the GRN row
        // down with it.
        await using var fixture = await SaleFixture.CreateSignedInAsync(
            labelPrinterFailureMode: PrinterFailureMode.FailEveryJob);
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-LABELFAIL-A");

        var grn = fixture.Resolve<IGoodsReceiptService>();

        var act = () => grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, null, ReceivedAt, Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 5m, Money.FromDecimal(1.00m))]));

        var result = await act.Should().NotThrowAsync(
            "a label printer fault must degrade with a warning, never abort or roll back a receipt "
            + "that has already posted stock");

        result.Subject.LabelPrintOutcome.Succeeded.Should().BeFalse();
        result.Subject.LabelPrintOutcome.FailureReason.Should().NotBeNullOrWhiteSpace();

        // The receipt, its line and the stock movement/balance are all still there: the failed
        // label print happened strictly after the business transaction committed, not inside it.
        (await fixture.CountAsync("SELECT COUNT(*) FROM goods_receipt WHERE id = " + result.Subject.Receipt.Id + ";"))
            .Should().Be(1);

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'GRN' AND ref_doc_id = " + result.Subject.Receipt.Id + ";"))
            .Should().Be(1);

        (await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";"))
            .Should().Be(Quantity.FromDecimal(5m, pieceUomId).ToScaled().ToString());

        Directory.Exists(fixture.LabelDirectory).Should().BeFalse(
            "FileLabelPrinter never writes a file when FailureMode is FailEveryJob");
    }

    [Fact]
    public async Task FR_4_7_ReceivingWithNoPurchaseOrderStillPostsAndAudits()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await ConfigureGrnSeriesAsync(fixture);
        var supplierId = await SeedSupplierAsync(fixture);
        var pieceUomId = await PieceUomIdAsync(fixture);
        var (variantId, _, _) = await SeedProductWithVariantAsync(fixture, "GRN-UNLINKED-A");

        var grn = fixture.Resolve<IGoodsReceiptService>();
        var result = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            supplierId, null, "INV-9001", ReceivedAt, Money.Zero, "First delivery",
            [new CreateGoodsReceiptLineCommand(variantId, pieceUomId, 5m, Money.FromDecimal(1.00m))]));

        result.Receipt.GrnNo.Should().Be("GRN-2026-000001");
        result.Receipt.PurchaseOrderId.Should().BeNull();

        (await fixture.ScalarAsync(
            "SELECT action || '|' || entity_type FROM audit_log WHERE action = 'GOODS_RECEIPT_CREATED' ORDER BY id DESC LIMIT 1;"))
            .Should().Be("GOODS_RECEIPT_CREATED|goods_receipt");

        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_type = 'GRN';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task AC_17_ACashierIsRefusedByTheGoodsReceiptServiceItself()
    {
        await using var fixture = await SaleFixture.CreateAsync();
        await ConfigureGrnSeriesAsync(fixture);

        await fixture.SignInAsSeededOwnerAsync();
        await fixture.Resolve<IUserAdministration>()
            .CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var goodsReceipts = fixture.Resolve<IGoodsReceiptService>();

        var list = () => goodsReceipts.ListAsync();
        var receive = () => goodsReceipts.ReceiveAsync(new CreateGoodsReceiptCommand(1, null, null, ReceivedAt, Money.Zero, null, []));

        await list.Should().ThrowAsync<NotAuthorisedException>();
        await receive.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM goods_receipt;")).Should().Be(0);
        fixture.TryResolve<GoodsReceiptService>().Should().BeNull(
            "the container must hand out only the role-decorated interface, never the concrete service");
    }

    // ---- Shared seeding -----------------------------------------------------------------------

    /// <summary>
    /// <c>SaleFixture</c> seeds only SALE and SHIFT numbering (P0-T06's own walking-skeleton
    /// seeder) - a goods receipt test configures its own GRN series here the same way the real
    /// till's first-run wizard would, through the same public port, never by inserting the row
    /// by hand (the same reasoning <c>PurchaseOrderServiceTests.SeedSupplierAsync</c> carries for
    /// the PO series).
    /// </summary>
    private static Task<bool> ConfigureGrnSeriesAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeedSupplierAsync(SaleFixture fixture, string name = "Ceylon Hardware Suppliers") =>
        await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand(name, null, null, null, null, null));

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<IUomMaintenance>().ListAsync()).Single(u => u.Name == "Piece").Id;

    private static async Task<long> ExemptTaxClassIdAsync(SaleFixture fixture) =>
        (await fixture.Resolve<ITaxClassMaintenance>().ListAsync()).Single(t => t.Name == "Exempt").Id;

    /// <summary>A boxed product: nails, sellable in the base piece unit or a box of 100 - the same shape <c>PurchaseOrderServiceTests.SeedBoxedNailsAsync</c> seeds.</summary>
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
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(0.10m)));

        await products.AddUomOptionAsync(
            productId,
            new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), SellingPrice: null));

        return (variantId, boxId, pieceUomId);
    }

    /// <summary>A plain single-unit product, priced at <paramref name="sellingPrice"/> per piece.</summary>
    private static async Task<(long VariantId, long ProductId, long PieceUomId)> SeedProductWithVariantAsync(
        SaleFixture fixture, string code, decimal sellingPrice = 1.00m)
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
            new SaveProductVariantCommand(code + "-A", EmptyAttributes, Money.FromDecimal(sellingPrice)));

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
            ReceivedAt));
    }
}
