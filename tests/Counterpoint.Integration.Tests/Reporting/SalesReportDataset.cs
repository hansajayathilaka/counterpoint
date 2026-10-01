using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The fixed, hand-worked trading history the P3-T05 report tests are checked against. Every
/// expected figure in those tests is worked out on paper from the table below - never read back
/// from the code under test.
/// </summary>
/// <remarks>
/// <para>
/// Shop pricing is exclusive (tax is added on top); every amount is a round number so no
/// rounding step enters the hand arithmetic. Times carry the shop's +05:30 offset, so the hour of
/// day is the wall-clock hour written here.
/// </para>
/// <code>
/// Products (all base unit Piece)
///   Bolt   category Fasteners, brand Bosch   price 100.00  cost  60.00  tax 10%
///   Drill  category Tools,     brand Makita  price 250.00  cost 150.00  tax 10%
///   Nail   no category, no brand             price  10.00  cost   4.00  tax  0%   also sold by the Box (12 pieces) at 100.00
///   Delivery: an open item, 50.00, tax at the shop default (0%), cost 0
///
/// Day one 2026-09-06 (shift 1, closed and rolled up at 20:00)
///   B1 10:05  Bolt x3 less 30.00 line discount, Drill x1, bill discount 26.00
///             subtotal 520.00  discounts 56.00  tax 25.65 + 23.75 = 49.40  total 543.40  cogs 330.00
///             net 494.00 (= 520.00 - 26.00)   tendered CASH 400.00 + CARD 143.40
///   B2 10:40  Nail x2 Box (24 pieces), Delivery x1
///             subtotal 250.00  tax 0  total 250.00  cogs 96.00   CASH 250.00
///   B3 14:20  Bolt x2
///             subtotal 200.00  tax 20.00  total 220.00  cogs 120.00   CARD 220.00
///   B4 15:00  Drill x1, then CANCELLED - must appear nowhere
///
/// Day two 2026-09-07 (shift 2, still open)
///   B5 09:30  Drill x2               subtotal 500.00  tax 50.00  total 550.00  cogs 300.00  CASH 550.00
///   B6 14:10  Bolt x1, Nail x5       subtotal 150.00  tax 10.00  total 160.00  cogs  80.00  CASH 160.00
///   R1 11:00  linked to B1, Bolt x1 SELLABLE "Changed mind"
///             (270.00 - 13.50 share) / 3 = 85.50   tax 8.55   refund 94.05 CASH
///   R2 11:30  linked to B5, Drill x1 DAMAGED "Cracked housing"
///             500.00 / 2 = 250.00   tax 25.00   refund 275.00 CASH
///   R3 16:00  unlinked, Bolt x1 SELLABLE at 100.00 "No receipt"
///             100.00   tax 10.00   refund 110.00 CARD
/// </code>
/// </remarks>
internal sealed record SalesReportDataset(
    long BoltVariantId,
    long DrillVariantId,
    long NailVariantId,
    long PieceUomId,
    long BoxUomId,
    long FastenersCategoryId,
    long ToolsCategoryId,
    long BoschBrandId,
    long MakitaBrandId,
    long OwnerId,
    CompletedSale B1,
    CompletedSale B2,
    CompletedSale B3,
    CompletedSale B4Cancelled,
    CompletedSale B5,
    CompletedSale B6,
    CreatedReturn R1,
    CreatedReturn R2,
    CreatedReturn R3)
{
    internal const string Owner = "owner";
    internal const string OwnerPassword = "till2026";

    internal static readonly TimeSpan ShopOffset = TimeSpan.FromHours(5.5);

    internal static readonly DateOnly DayOne = new(2026, 9, 6);
    internal static readonly DateOnly DayTwo = new(2026, 9, 7);

    internal static DateTimeOffset At(int day, int hour, int minute) =>
        new(2026, 9, day, hour, minute, 0, ShopOffset);

    /// <summary>
    /// Stops a shift close reaching into the backup machinery - these tests are about reports,
    /// exactly as <c>ReportQueryLayerTests</c> explains. The fixture must have been built with
    /// <c>includeBackup: true</c> so <c>CloseShiftHandler</c> has its trigger to call.
    /// </summary>
    internal static Task DisableBackupOnShiftCloseAsync(SaleFixture fixture) =>
        fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot with
        {
            Backup = snapshot.Backup with { BackupOnShiftClose = false },
        });

    internal static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    /// <summary>Builds the whole history above on a fresh, signed-in-as-owner fixture.</summary>
    internal static async Task<SalesReportDataset> BuildAsync(SaleFixture fixture)
    {
        await SeedReturnNumberSequenceAsync(fixture);
        await DisableBackupOnShiftCloseAsync(fixture);
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax: false);

        var session = fixture.Resolve<ISession>();
        var owner = session.CurrentUser!;
        var shiftOne = session.ShiftId!.Value;

        var catalogue = await SeedCatalogueAsync(fixture);

        // ---- Day one ---------------------------------------------------------------------------
        var b1 = await SellAsync(
            fixture,
            At(6, 10, 5),
            [
                new SaleLineRequest(catalogue.Bolt, 3m, Discount: DiscountInput.OfAmount(Money.FromDecimal(30.00m))),
                new SaleLineRequest(catalogue.Drill, 1m),
            ],
            billDiscount: DiscountInput.OfAmount(Money.FromDecimal(26.00m)),
            (TenderTypes.Cash, 400.00m),
            (TenderTypes.Card, 143.40m));
        b1.Total.Should().Be(Money.FromDecimal(543.40m), "the hand-worked dataset depends on it");

        var b2 = await SellAsync(
            fixture,
            At(6, 10, 40),
            [
                new SaleLineRequest(catalogue.Nail, 2m, UomId: catalogue.Box),
                new SaleLineRequest(
                    null, 1m, UomId: catalogue.Piece, OpenItemDescription: "Delivery", OpenItemUnitPrice: Money.FromDecimal(50.00m)),
            ],
            billDiscount: null,
            (TenderTypes.Cash, 250.00m));
        b2.Total.Should().Be(Money.FromDecimal(250.00m));

        var b3 = await SellAsync(
            fixture, At(6, 14, 20), [new SaleLineRequest(catalogue.Bolt, 2m)], null, (TenderTypes.Card, 220.00m));
        b3.Total.Should().Be(Money.FromDecimal(220.00m));

        var b4 = await SellAsync(
            fixture, At(6, 15, 0), [new SaleLineRequest(catalogue.Drill, 1m)], null, (TenderTypes.Cash, 275.00m));
        b4.Total.Should().Be(Money.FromDecimal(275.00m));
        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(b4.SaleId, "Rung up by mistake", At(6, 15, 10)));

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftOne, owner.Id, Money.FromDecimal(1300.00m), At(6, 20, 0), Note: "P3-T05 fixture"));

        // ---- Day two ---------------------------------------------------------------------------
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(owner.Id, Money.Zero, At(7, 8, 30)));
        var shiftTwo = session.ShiftId!.Value;

        var b5 = await SellAsync(
            fixture, At(7, 9, 30), [new SaleLineRequest(catalogue.Drill, 2m)], null, (TenderTypes.Cash, 550.00m));
        b5.Total.Should().Be(Money.FromDecimal(550.00m));

        var b6 = await SellAsync(
            fixture,
            At(7, 14, 10),
            [new SaleLineRequest(catalogue.Bolt, 1m), new SaleLineRequest(catalogue.Nail, 5m)],
            null,
            (TenderTypes.Cash, 160.00m));
        b6.Total.Should().Be(Money.FromDecimal(160.00m));

        var r1 = await ReturnAsync(
            fixture, b1, lineNo: 1, quantityBase: 1m, ReturnDisposition.Sellable, "Changed mind", At(7, 11, 0), shiftTwo, owner.Id);
        r1.TotalRefund.Should().Be(Money.FromDecimal(94.05m));

        var r2 = await ReturnAsync(
            fixture, b5, lineNo: 1, quantityBase: 1m, ReturnDisposition.Damaged, "Cracked housing", At(7, 11, 30), shiftTwo, owner.Id);
        r2.TotalRefund.Should().Be(Money.FromDecimal(275.00m));

        await fixture.Resolve<ISettings>().UpdateAsync(
            settings => settings with { Policy = settings.Policy with { AllowUnlinkedReturns = true } });
        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(ReturnPolicyAuditActions.UnlinkedReturn, "No receipt kept.", Owner, OwnerPassword));
        var r3 = await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(new CreateUnlinkedReturnCommand(
            owner.Id,
            shiftTwo,
            At(7, 16, 0),
            [new UnlinkedReturnLineRequest(
                catalogue.Bolt, Quantity.FromDecimal(1m, catalogue.Piece), Money.FromDecimal(100.00m), ReturnDisposition.Sellable, "No receipt")],
            RefundMethod.Card,
            token,
            "No receipt kept, regular customer."));
        r3.TotalRefund.Should().Be(Money.FromDecimal(110.00m));

        return new SalesReportDataset(
            catalogue.Bolt,
            catalogue.Drill,
            catalogue.Nail,
            catalogue.Piece,
            catalogue.Box,
            catalogue.Fasteners,
            catalogue.Tools,
            catalogue.Bosch,
            catalogue.Makita,
            owner.Id,
            b1, b2, b3, b4, b5, b6, r1, r2, r3);
    }

    internal static async Task<CompletedSale> SellAsync(
        SaleFixture fixture,
        DateTimeOffset soldAt,
        IReadOnlyList<SaleLineRequest> lines,
        DiscountInput? billDiscount,
        params (string Tender, decimal Amount)[] tenders)
    {
        var session = fixture.Resolve<ISession>();

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            session.CurrentUser!.Id,
            session.ShiftId!.Value,
            soldAt,
            lines,
            [.. tenders.Select(tender => new TenderRequest(tender.Tender, Money.FromDecimal(tender.Amount)))],
            BillDiscount: billDiscount));
    }

    internal static async Task<CreatedReturn> ReturnAsync(
        SaleFixture fixture,
        CompletedSale sale,
        int lineNo,
        decimal quantityBase,
        ReturnDisposition disposition,
        string reason,
        DateTimeOffset returnedAt,
        long shiftId,
        long userId)
    {
        var saleLineId = await fixture.CountAsync(
            "SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId.ToString(CultureInfo.InvariantCulture)
            + " AND line_no = " + lineNo.ToString(CultureInfo.InvariantCulture) + ";");

        return await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            userId,
            shiftId,
            returnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(quantityBase, saleLineId), disposition, reason)],
            RefundMethod.Cash));
    }

    private sealed record Catalogue(
        long Bolt, long Drill, long Nail, long Piece, long Box, long Fasteners, long Tools, long Bosch, long Makita);

    private static Task<Catalogue> SeedCatalogueAsync(SaleFixture fixture)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();
        var seededAt = At(1, 8, 0);

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var piece = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var box = new Uom { Name = "Box of 12", Symbol = "box", DecimalPlaces = 0, Active = true };
            var fasteners = new Category { Name = "Fasteners", Active = true };
            var tools = new Category { Name = "Tools", Active = true };
            var bosch = new Brand { Name = "Bosch", Active = true };
            var makita = new Brand { Name = "Makita", Active = true };
            var ten = new TaxClass { Name = "Ten percent (P3-T05)", Rate = TaxRate.FromPercent(10m), Active = true };
            var zero = new TaxClass { Name = "Zero rated (P3-T05)", Rate = TaxRate.FromPercent(0m), Active = true };
            context.AddRange(box, fasteners, tools, bosch, makita, ten, zero);
            await context.SaveChangesAsync(token);

            async Task<long> ProductAsync(
                string code, string name, long? categoryId, long? brandId, long taxClassId, decimal price, decimal cost)
            {
                var product = new Product
                {
                    Code = code,
                    Name = name,
                    NameAlt = null,
                    CategoryId = categoryId,
                    BrandId = brandId,
                    BaseUomId = piece,
                    Type = "STANDARD",
                    TaxClassId = taxClassId,
                    CostAvg = Money.FromDecimal(cost),
                    ReorderLevel = 0,
                    ReorderQty = 0,
                    Location = "A1",
                    NonReturnable = false,
                    MinSellQty = 0,
                    MaxDiscountRate = null,
                    WarrantyDays = null,
                    Notes = null,
                    ImagePath = null,
                    Active = true,
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt,
                };
                context.Add(product);
                await context.SaveChangesAsync(token);

                context.Add(new ProductUom
                {
                    ProductId = product.Id,
                    UomId = piece,
                    ConversionFactor = UomConversion.Base.ToScaled(),
                    SellingPrice = null,
                    IsBase = true,
                });
                await context.SaveChangesAsync(token);

                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = code + "-A",
                    Attributes = "{}",
                    Price = Money.FromDecimal(price),
                    Active = true,
                    CreatedAt = seededAt,
                };
                context.Add(variant);
                await context.SaveChangesAsync(token);

                await ledger.PostAsync(
                    new StockPosting(
                        variant.Id, "OPENING", Quantity.FromDecimal(1000m, piece), Money.FromDecimal(cost),
                        "OPENING", RefDocId: null, userId, seededAt),
                    token);

                return product.Id;
            }

            var boltProduct = await ProductAsync("RPT-BOLT", "Bolt", fasteners.Id, bosch.Id, ten.Id, 100.00m, 60.00m);
            var drillProduct = await ProductAsync("RPT-DRILL", "Drill", tools.Id, makita.Id, ten.Id, 250.00m, 150.00m);
            var nailProduct = await ProductAsync("RPT-NAIL", "Nail", null, null, zero.Id, 10.00m, 4.00m);

            // The Box: 12 pieces, priced at 100.00 (not 12 x 10.00 = 120.00) so the selling unit's own
            // price - not the base price times the factor - is what the bill must snapshot.
            context.Add(new ProductUom
            {
                ProductId = nailProduct,
                UomId = box.Id,
                ConversionFactor = UomConversion.FromDecimal(12m).ToScaled(),
                SellingPrice = Money.FromDecimal(100.00m),
                IsBase = false,
            });
            await context.SaveChangesAsync(token);

            long VariantOf(long productId) => context.Set<ProductVariant>().Where(v => v.ProductId == productId).Select(v => v.Id).First();

            return new Catalogue(
                VariantOf(boltProduct),
                VariantOf(drillProduct),
                VariantOf(nailProduct),
                piece,
                box.Id,
                fasteners.Id,
                tools.Id,
                bosch.Id,
                makita.Id);
        });
    }
}
