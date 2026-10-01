using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The P3-T06 stock-side history, laid over <see cref="SalesReportDataset"/> (which is built first and
/// left exactly as P3-T05 defines it). Every expected figure in the P3-T06 tests is worked out on paper
/// from the tables below - never read back from the code under test.
/// </summary>
/// <remarks>
/// <para>
/// Shop times carry the +05:30 offset. Moving-average costs are chosen so every blend divides exactly
/// (no rounding step enters the hand arithmetic): each receipt that lands on existing stock either
/// brings the average to a terminating figure or is priced at the current average.
/// </para>
/// <code>
/// After the P3-T05 history (see SalesReportDataset):
///   Bolt   1000 opening (Sep 1) - 3 (B1) - 2 (B3) - 1 (B6) + 1 (R1) + 1 (R3) = 996 at 60.00
///   Drill  1000 opening - 1 (B1) - 1 (B4) + 1 (B4 cancelled) - 2 (B5)        = 997 at 150.00  (R2 damaged: no stock)
///   Nail   1000 opening - 24 (B2, two boxes of 12) - 5 (B6)                   = 971 at 4.00
///   Seeded SKEL-001-A                                                          = 100 at 9.00, price 12.50, barcode 5901234123457
///
/// Posted after it, in this call order (ledger id order). G1 is back-dated: it is entered after the
/// Sep 6-7 trading but carries its real receipt date, Sep 3.
///   G1  Sep 3  10:00  Acme Fasteners   GRN-2026-000001  Bolt 100 @ 54.52 tax 545.20; Drill 10 @ 99.65 tax 99.65; freight 0
///                     subtotal 6448.50 tax 644.85 total 7093.35.  Bolt avg (996x60 + 100x54.52)/1096 = 59.50; Drill avg
///                     (997x150 + 10x99.65)/1007 = 149.50
///   A1  Sep 8  09:00  Bolt   ADJUSTMENT -4  "Stock count correction"  at 59.50  = -238.00
///   A2  Sep 8  23:59:59 Bolt DAMAGE     -6  "Water damage"            at 59.50  = -357.00
///   A3  Sep 8  10:00  Drill  ADJUSTMENT +3  "Found in back store"     at 149.50 = +448.50
///   A4  Sep 9  00:00:00 Drill DAMAGE    -2  "Water damage"            at 149.50 = -299.00
///   A5  Sep 9  11:30  Drill  ADJUSTMENT -1  "Stock count correction"  at 149.50 = -149.50
///   G2  Sep 10 09:00  Zenith Tools     GRN-2026-000002  Washer 100 @ 2.00 tax 20.00; Gasket 10 @ 10.00 tax 10.00; freight 15.00
///                     freight shares 10.00 / 5.00; subtotal 300.00 tax 30.00 other 15.00 total 345.00
///                     Washer landed 210.00 / 100 = 2.10; Gasket landed 105.00 / 10 = 10.50, avg (10x8.00 + 10x10.50)/20 = 9.25
///   B7  Sep 11 10:00  Hinge x1 15.00 CASH, cancelled 10:10 (a cancelled bill is not a sale)
///   ST  Sep 11 10:00  stock take scope BRAND:Makita, Drill counted 1000 (system 1007): STOCK_TAKE -7   ST-2026-000001
///   G3  Sep 12 23:59:59 Zenith Tools   GRN-2026-000003  Washer 100 @ 3.20, no tax, no freight; Washer avg (100x2.10 + 100x3.20)/200 = 2.65
///   G4  Sep 13 00:00:00 Acme Fasteners GRN-2026-000004  Bolt 50 @ 65.18, no tax, no freight: Bolt avg (1086x59.50 + 50x65.18)/1136 = 59.75
///
/// New catalogue (all base unit Piece, tax Exempt): Washer (category "Machine screws", a child of Fasteners, price 5.00,
/// location B2, no opening), Gasket (no category, price 20.00, location B2, opening 10 @ 8.00 on Sep 2 08:00),
/// Hinge (category Tools, price 15.00, location C3, opening 10 @ 8.00 on Sep 2 08:00), Cord (no category, price 12.49,
/// location C1, a Fractional (DECIMAL) product, opening 12.3456 @ 7.7777 on Sep 4 08:00 - a fractional quantity times a cost
/// that does not divide), Rivet (Standard, price 1.00, location D4, never stocked - no movement, no balance row) and Labour
/// (a Service, price 40.00, location D4 - not stock-tracked).
/// Reorder levels / quantities: Bolt 1200/500, Drill 1090/200, Washer 300/400, Gasket 50/100, Hinge 45/100, others 0.
///
/// Balances at the end: Bolt 1136 at 59.75, Drill 1000 at 149.50, Nail 971 at 4.00, SKEL 100 at 9.00, Washer 200 at 2.65,
/// Gasket 20 at 9.25, Hinge 10 at 8.00, Cord 12.3456 at 7.7777.
/// </code>
/// </remarks>
internal sealed record StockCashDataset(
    SalesReportDataset Sales,
    long SkeletonVariantId,
    long AcmeId,
    long ZenithId,
    long WasherVariantId,
    long GasketVariantId,
    long HingeVariantId,
    long CordVariantId,
    long RivetVariantId,
    long LabourVariantId,
    long MachineScrewsCategoryId)
{
    internal const string ShopName = "Kandy Hardware & Tools";
    internal const string TaxRegistration = "TIN-204-118-77";
    internal const string TaxLabel = "VAT";

    internal const string SkeletonSku = "SKEL-001-A";
    internal const string SkeletonBarcode = "5901234123457";

    internal const string Grn1 = "GRN-2026-000001";
    internal const string Grn2 = "GRN-2026-000002";
    internal const string Grn3 = "GRN-2026-000003";
    internal const string Grn4 = "GRN-2026-000004";
    internal const string StockTakeNo = "ST-2026-000001";

    internal static DateTimeOffset At(int day, int hour, int minute, int second = 0) =>
        new(2026, 9, day, hour, minute, second, SalesReportDataset.ShopOffset);

    /// <summary>Builds the P3-T05 history, then the stock-side history above, on a signed-in-as-owner fixture.</summary>
    internal static async Task<StockCashDataset> BuildAsync(SaleFixture host)
    {
        var sales = await SalesReportDataset.BuildAsync(host);

        // The tax report's header is read from settings (Q-02: no regime is baked in).
        await host.Resolve<ISettings>().UpdateAsync(settings => settings with
        {
            Shop = settings.Shop with { Name = ShopName, TaxRegistrationNumber = TaxRegistration },
            Tax = settings.Tax with { TaxLabel = TaxLabel },
        });

        var sequences = host.Resolve<INumberSequenceConfiguration>();
        await sequences.ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("STOCK_TAKE", "ST-", "{prefix}{yyyy}-{n:000000}", 1);

        var userId = await host.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var skeleton = await host.CountAsync("SELECT id FROM product_variant WHERE sku = '" + SkeletonSku + "';");

        var suppliers = host.Resolve<ISupplierMaintenance>();
        var acme = await suppliers.CreateAsync(new SaveSupplierCommand("Acme Fasteners", null, null, null, null, null));
        var zenith = await suppliers.CreateAsync(new SaveSupplierCommand("Zenith Tools", null, null, null, null, null));

        var grn = host.Resolve<IGoodsReceiptService>();

        // G1, back-dated (entered after the Sep 6-7 trading, received on Sep 3).
        var g1 = await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            acme, null, "AC-001", At(3, 10, 0), Money.Zero, null,
            [
                new CreateGoodsReceiptLineCommand(sales.BoltVariantId, sales.PieceUomId, 100m, Money.FromDecimal(54.52m), Money.FromDecimal(545.20m)),
                new CreateGoodsReceiptLineCommand(sales.DrillVariantId, sales.PieceUomId, 10m, Money.FromDecimal(99.65m), Money.FromDecimal(99.65m)),
            ]));
        g1.Receipt.Total.Should().Be(Money.FromDecimal(7093.35m), "the hand-worked dataset depends on it");

        // New catalogue.
        var products = host.Resolve<IProductMaintenance>();
        var exempt = (await host.Resolve<ITaxClassMaintenance>().ListAsync()).Single(tax => tax.Name == "Exempt").Id;
        var screws = await host.Resolve<ICategoryMaintenance>()
            .CreateAsync(new SaveCategoryCommand("Machine screws", sales.FastenersCategoryId));

        async Task<long> AddAsync(
            string code, string name, long? category, string location, decimal price, ProductType type = ProductType.Standard)
        {
            var productId = await products.CreateAsync(new SaveProductCommand(
                code, name, null, category, null, sales.PieceUomId, type, exempt, location,
                NonReturnable: false, WarrantyDays: null, Notes: null, MaxDiscountRate: null, ConfirmDuplicate: true));

            return await products.CreateVariantAsync(
                productId,
                new SaveProductVariantCommand(code + "-A", new Dictionary<string, string>(), Money.FromDecimal(price)));
        }

        var washer = await AddAsync("EXT-WASHER", "Washer", screws, "B2", 5.00m);
        var gasket = await AddAsync("EXT-GASKET", "Gasket", null, "B2", 20.00m);
        var hinge = await AddAsync("EXT-HINGE", "Hinge", sales.ToolsCategoryId, "C3", 15.00m);
        var cord = await AddAsync("EXT-CORD", "Cord", null, "C1", 12.49m, ProductType.Fractional);

        // Never stocked (no ledger movement, no balance row) and a service (not stock-tracked at all).
        var rivet = await AddAsync("EXT-RIVET", "Rivet", null, "D4", 1.00m);
        var labour = await AddAsync("EXT-LABOUR", "Labour", null, "D4", 40.00m, ProductType.Service);

        var ledger = host.Resolve<IStockLedger>();

        Task OpenAsync(long variant, decimal quantity, decimal cost, DateTimeOffset at) =>
            ledger.PostAsync(new StockPosting(
                variant, "OPENING", Quantity.FromDecimal(quantity, sales.PieceUomId), Money.FromDecimal(cost),
                "OPENING", RefDocId: null, userId, at));

        await OpenAsync(gasket, 10m, 8.00m, At(2, 8, 0));
        await OpenAsync(hinge, 10m, 8.00m, At(2, 8, 0));
        await OpenAsync(cord, 12.3456m, 7.7777m, At(4, 8, 0));

        Task SetReorderAsync(long variant, decimal level, decimal quantity) =>
            host.ExecuteAsync(
                "UPDATE product SET reorder_level = " + Quantity.FromDecimal(level, sales.PieceUomId).ToScaled()
                + ", reorder_qty = " + Quantity.FromDecimal(quantity, sales.PieceUomId).ToScaled()
                + " WHERE id = (SELECT product_id FROM product_variant WHERE id = " + variant + ");");

        await SetReorderAsync(sales.BoltVariantId, 1200m, 500m);
        await SetReorderAsync(sales.DrillVariantId, 1090m, 200m);
        await SetReorderAsync(washer, 300m, 400m);
        await SetReorderAsync(gasket, 50m, 100m);
        await SetReorderAsync(hinge, 45m, 100m);

        // Adjustments and damage.
        var adjust = host.Resolve<IPostAdjustment>();
        await adjust.AdjustAsync(new AdjustmentCommand(sales.BoltVariantId, -4m, null, "Stock count correction", At(8, 9, 0)));
        await adjust.WriteOffDamageAsync(new DamageCommand(sales.BoltVariantId, 6m, "Water damage", At(8, 23, 59, 59)));
        await adjust.AdjustAsync(new AdjustmentCommand(sales.DrillVariantId, 3m, null, "Found in back store", At(8, 10, 0)));
        await adjust.WriteOffDamageAsync(new DamageCommand(sales.DrillVariantId, 2m, "Water damage", At(9, 0, 0)));
        await adjust.AdjustAsync(new AdjustmentCommand(sales.DrillVariantId, -1m, null, "Stock count correction", At(9, 11, 30)));

        // G2: freight across two lines.
        await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            zenith, null, "ZT-77", At(10, 9, 0), Money.FromDecimal(15.00m), null,
            [
                new CreateGoodsReceiptLineCommand(washer, sales.PieceUomId, 100m, Money.FromDecimal(2.00m), Money.FromDecimal(20.00m)),
                new CreateGoodsReceiptLineCommand(gasket, sales.PieceUomId, 10m, Money.FromDecimal(10.00m), Money.FromDecimal(10.00m)),
            ]));

        // B7: a Hinge sold and cancelled the same day - it never sold.
        var b7 = await SalesReportDataset.SellAsync(
            host, At(11, 10, 0), [new SaleLineRequest(hinge, 1m)], null, (TenderTypes.Cash, 15.00m));
        await host.Resolve<ICancelSale>().CancelAsync(new CancelSaleCommand(b7.SaleId, "Rung up by mistake", At(11, 10, 10)));

        // Stock take over the Makita brand (Drill only): counts 1000 against a system figure of 1007.
        var stockTakes = host.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand("BRAND:" + sales.MakitaBrandId, At(11, 9, 0)));
        started.StockTakeNo.Should().Be(StockTakeNo);
        await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, sales.DrillVariantId, 1000m, At(11, 9, 30)));
        await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, At(11, 10, 0)));

        // G3 and G4 straddle midnight on purpose (the day boundary of a range).
        await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            zenith, null, null, At(12, 23, 59, 59), Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(washer, sales.PieceUomId, 100m, Money.FromDecimal(3.20m))]));
        await grn.ReceiveAsync(new CreateGoodsReceiptCommand(
            acme, null, null, At(13, 0, 0), Money.Zero, null,
            [new CreateGoodsReceiptLineCommand(sales.BoltVariantId, sales.PieceUomId, 50m, Money.FromDecimal(65.18m))]));

        return new StockCashDataset(sales, skeleton, acme, zenith, washer, gasket, hinge, cord, rivet, labour, screws);
    }
}
