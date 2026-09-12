using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Returns;

/// <summary>
/// Linked returns, end to end: find the original bill, pick lines and quantities, choose a
/// disposition, refund at the price originally paid (SRS FR-5.1-FR-5.10, AC-03, AC-06,
/// task P2-T02).
/// </summary>
public sealed class CreateReturnTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_03_APartialReturnRefundsAtTheOriginalPaidPriceRestocksOnlySellableAndPrintsAReceipt()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var sale = await CompleteOneAsync(fixture, quantity: 5m);
        var qtyOnHandAfterSale = await fixture.CountAsync("SELECT qty_base FROM stock_balance;");

        // The single most common bug in POS returns (task P2-T02 risk note): the shelf price
        // moves between the sale and the return, and the refund must not follow it.
        await fixture.ExecuteAsync("UPDATE product_variant SET price = 999999 WHERE id = "
            + await SeededVariantIdAsync(fixture) + ";");

        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Sellable, "Customer changed mind")],
            RefundMethod.Cash));

        // 2 pieces at the ORIGINAL 12.50, never the 99.9999 the price was just changed to.
        created.TotalRefund.Should().Be(Money.FromDecimal(25.00m));

        var qtyOnHandAfterReturn = await fixture.CountAsync("SELECT qty_base FROM stock_balance;");
        (qtyOnHandAfterReturn - qtyOnHandAfterSale).Should().Be(20000, "2 pieces, scaled x10 000, restocked");

        var qtyReturned = await fixture.ScalarAsync(
            "SELECT qty_returned FROM sale_line WHERE id = " + saleLineId + ";");
        qtyReturned.Should().Be("20000");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be(1);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM sale_return_line WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be(1);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be(1);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'RETURN_COMPLETED';"))
            .Should().Be(1);

        // A correct return receipt: one print_job row, queued rather than printed inside the
        // transaction (CLAUDE.md invariant 7), carrying a non-empty rendered byte stream.
        var printJob = await fixture.ScalarAsync(
            "SELECT doc_type || '|' || doc_id || '|' || LENGTH(payload) FROM print_job WHERE id = "
            + created.PrintJobId + ";");
        printJob.Should().StartWith("RETURN|" + created.SaleReturnId + "|").And.NotEndWith("|0");
    }

    [Fact]
    public async Task APriceChangeBetweenSaleAndReturnDoesNotAffectTheRefundAmount()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var sale = await CompleteOneAsync(fixture, quantity: 1m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        await fixture.ExecuteAsync("UPDATE product_variant SET price = 1 WHERE id = "
            + await SeededVariantIdAsync(fixture) + ";");

        var created = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong item")],
            RefundMethod.Cash));

        created.TotalRefund.Should().Be(
            Money.FromDecimal(12.50m), "the price paid, not the 1.00 the shelf price was just changed to");

        var storedUnitPrice = await fixture.ScalarAsync(
            "SELECT unit_price FROM sale_return_line WHERE sale_return_id = " + created.SaleReturnId + ";");
        storedUnitPrice.Should().Be("125000", "sale_return_line.unit_price is the price ORIGINALLY paid (AC-03)");
    }

    [Fact]
    public async Task AC_06_CumulativeOverReturnAcrossTwoSeparateReturnsAgainstTheSameLineIsImpossible()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var sale = await CompleteOneAsync(fixture, quantity: 3m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        // First return: 2 of the 3 sold. Allowed.
        await fixture.Resolve<ICreateReturn>().CreateAsync(await ReturnCommandAsync(fixture, sale.SaleId, saleLineId, 2m));

        // Second, separate return: another 2 would total 4 against 3 sold. Never allowed, and
        // there is no override parameter anywhere that could let it through (task P2-T01 risk
        // note, AC-06).
        var overReturn = async () => await fixture.Resolve<ICreateReturn>().CreateAsync(
            await ReturnCommandAsync(fixture, sale.SaleId, saleLineId, 2m));

        var exception = await overReturn.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<Counterpoint.Domain.Returns.ReturnEligibility.Denied>();
        exception.Which.Action.Should().BeNull("AC-06 never offers an override to ask for");

        // Exactly what remains (1 more) is still allowed - the guard is cumulative, not "no
        // second return at all".
        await fixture.Resolve<ICreateReturn>().CreateAsync(await ReturnCommandAsync(fixture, sale.SaleId, saleLineId, 1m));

        var qtyReturned = await fixture.ScalarAsync("SELECT qty_returned FROM sale_line WHERE id = " + saleLineId + ";");
        qtyReturned.Should().Be("30000", "all 3 sold, and not one thousandth more");

        // Now fully returned: even the smallest further request is refused.
        var thirdAttempt = async () => await fixture.Resolve<ICreateReturn>().CreateAsync(
            await ReturnCommandAsync(fixture, sale.SaleId, saleLineId, 1m));
        await thirdAttempt.Should().ThrowAsync<ReturnNotEligibleException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return WHERE original_sale_id = " + sale.SaleId + ";"))
            .Should().Be(2, "only the two that succeeded ever committed - a refused attempt writes nothing");
    }

    [Fact]
    public async Task DamagedDispositionDoesNotIncreaseSellableStock()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var sale = await CompleteOneAsync(fixture, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");
        var qtyOnHandAfterSale = await fixture.CountAsync("SELECT qty_base FROM stock_balance;");

        var created = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Damaged, "Damaged in transit")],
            RefundMethod.Cash));

        // Stock never moved: FR-5.8's "not added back to sellable stock" holds exactly, not
        // approximately - the balance the sale left behind is the balance the return leaves too.
        var qtyOnHandAfterReturn = await fixture.CountAsync("SELECT qty_base FROM stock_balance;");
        qtyOnHandAfterReturn.Should().Be(qtyOnHandAfterSale);

        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'RETURN';"))
            .Should().Be(0, "a DAMAGED line posts no stock movement at all - see CreateReturnHandler's own remarks");

        // The customer is still refunded - disposition changes what happens to the stock, never
        // whether the money moves.
        created.TotalRefund.Should().Be(Money.FromDecimal(25.00m));

        var disposition = await fixture.ScalarAsync(
            "SELECT disposition FROM sale_return_line WHERE sale_return_id = " + created.SaleReturnId + ";");
        disposition.Should().Be("DAMAGED");
    }

    [Fact]
    public async Task ScanningTheReceiptQrOpensTheCorrectBill()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var sale = await CompleteOneAsync(fixture, quantity: 4m);

        // The bill number is exactly what the original receipt's own barcode encodes
        // (EscPosSaleReceiptRenderer, ReceiptNode.Barcode(receipt.BillNo), P0-T05/P1-T11) - so
        // "scan the QR" and "look up by bill number" are the same lookup by construction. Real
        // scanner hardware is Devices/HW-T02's; this is the Application-layer half.
        var found = await fixture.Resolve<IReturnableSaleLookup>().FindByBillNoAsync(sale.BillNo);

        found.Should().NotBeNull();
        found!.SaleId.Should().Be(sale.SaleId);
        found.BillNo.Should().Be(sale.BillNo);
        found.Lines.Should().ContainSingle();
        found.Lines[0].QtySoldBase.Value.Should().Be(4m);
        found.Lines[0].QtyAvailableBase.Value.Should().Be(4m, "nothing has been returned yet");

        (await fixture.Resolve<IReturnableSaleLookup>().FindByBillNoAsync("INV-2026-999999"))
            .Should().BeNull("a bill number nobody scanned or typed correctly finds nothing, not the wrong bill");
    }

    [Fact]
    public async Task ReturnTotalsReconcileSumOfReturnLinesMinusFeeEqualsRefundPayments()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        // A restocking fee and a non-trivial tax rate - the case where the reconciliation
        // identity is not trivially true because everything happens to be zero.
        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { RestockingFeeRate = Percentage.FromPercent(10m) } });

        var variantId = await SeedTaxedVariantAsync(fixture);
        var sale = await CompleteTaxedAsync(fixture, variantId, quantity: 3m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Sellable, "Too many")],
            RefundMethod.Cash));

        created.TotalRefund.IsPositive.Should().BeTrue();

        var id = created.SaleReturnId.ToString(CultureInfo.InvariantCulture);

        (await fixture.ScalarAsync(
            "SELECT (SELECT SUM(line_refund) + SUM(tax) FROM sale_return_line WHERE sale_return_id = " + id + ") "
            + "- (SELECT restocking_fee FROM sale_return WHERE id = " + id + ") "
            + "= (SELECT total_refund FROM sale_return WHERE id = " + id + ");"))
            .Should().Be("1", "sum(return lines) - fee == total_refund, over the rows as stored");

        (await fixture.ScalarAsync(
            "SELECT (SELECT total_refund FROM sale_return WHERE id = " + id + ") "
            + "= -(SELECT SUM(amount) FROM payment WHERE sale_return_id = " + id + ");"))
            .Should().Be("1", "total_refund == -sum(refund payments), over the rows as stored");

        (await fixture.ScalarAsync(
            "SELECT restocking_fee > 0 FROM sale_return WHERE id = " + id + ";"))
            .Should().Be("1", "the 10% fee actually applied, so this is not a vacuous zero-everything identity");
    }

    private static async Task<CreateReturnCommand> ReturnCommandAsync(
        SaleFixture fixture, long saleId, long saleLineId, decimal quantity) => new(
        saleId,
        await SeededUserIdAsync(fixture),
        await SeededShiftIdAsync(fixture),
        ReturnedAt,
        [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(quantity, saleLineId), ReturnDisposition.Sellable, "Test")],
        RefundMethod.Cash);

    private static async Task<CompletedSale> CompleteOneAsync(SaleFixture fixture, decimal quantity)
    {
        var lines = new List<SaleLineRequest> { new(await SeededVariantIdAsync(fixture), quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                await SeededUserIdAsync(fixture),
                await SeededShiftIdAsync(fixture),
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    private static async Task<CompletedSale> CompleteTaxedAsync(SaleFixture fixture, long variantId, decimal quantity)
    {
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                await SeededUserIdAsync(fixture),
                await SeededShiftIdAsync(fixture),
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    /// <summary>
    /// The real till always has this row before a return can exist -
    /// <c>FirstRunSetupService.ConfigureNumberingAsync</c> creates every FR-10.4 series, RETURN
    /// included, during the wizard the shop actually runs. <c>SaleFixture</c> seeds only SALE and
    /// SHIFT (<c>FirstRunSeeder</c>, the P0-T06 walking-skeleton seeder) to keep every other
    /// test's setup cheap, so a return test creates its own RETURN series here the same way
    /// <c>ISettings.SaveAsync</c>/<c>FirstRunSetupService</c> would - through the same public
    /// port, never by inserting the row by hand. The same technique
    /// <c>PurchaseOrderServiceTests.SeedSupplierAsync</c> uses for its own PO series (task
    /// P2-T06).
    /// </summary>
    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

    /// <summary>
    /// Adds a second product to the seeded catalogue, taxed at a rate that is neither zero nor
    /// round - the same technique <c>TaxedSaleTests</c> uses, so that a reconciliation identity
    /// checked here is not trivially true over an all-zero tax column.
    /// </summary>
    private static Task<long> SeedTaxedVariantAsync(SaleFixture fixture)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<Counterpoint.Application.Abstractions.Persistence.IStockLedger>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var taxClass = new TaxClass
            {
                Name = "Standard rated",
                Rate = TaxRate.FromPercent(8.25m),
                Active = true,
            };

            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = "TAXED-RTN-001",
                Name = "Brass elbow 15mm",
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClass.Id,
                CostAvg = Money.FromDecimal(7.00m),
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = "B1",
                NonReturnable = false,
                MinSellQty = 0,
                MaxDiscountRate = null,
                WarrantyDays = null,
                Notes = null,
                ImagePath = null,
                Active = true,
                CreatedAt = SoldAt,
                UpdatedAt = SoldAt,
            };

            context.Add(product);
            await context.SaveChangesAsync(token);

            context.Add(new ProductUom
            {
                ProductId = product.Id,
                UomId = uomId,
                ConversionFactor = UomConversion.Base.ToScaled(),
                SellingPrice = null,
                IsBase = true,
            });
            await context.SaveChangesAsync(token);

            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = "TAXED-RTN-001-A",
                Attributes = """{"size":"15mm"}""",
                Price = Money.FromDecimal(12.34m),
                Active = true,
                CreatedAt = SoldAt,
            };

            context.Add(variant);
            await context.SaveChangesAsync(token);

            await ledger.PostAsync(
                new StockPosting(
                    variant.Id,
                    "OPENING",
                    Quantity.FromDecimal(100m, uomId),
                    Money.FromDecimal(7.00m),
                    "OPENING",
                    RefDocId: null,
                    userId,
                    SoldAt),
                token);

            return variant.Id;
        });
    }
}
