using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// The shop's pricing mode (SRS FR-10.3) and the bill discount's effect on the tax base
/// (SRS FR-3.17, §10.1), over bills as stored and receipts as rebuilt from them.
/// </summary>
/// <remarks>
/// Every other taxed-sale test in this project was written against the exclusive arithmetic,
/// while the shipped default is inclusive (<c>tax.prices_include_tax = true</c>) - so for as long
/// as the seeded catalogue was zero rated, a sale path that ignored the setting charged the right
/// amount by accident. These tests use a real rate in both modes.
/// </remarks>
public sealed class PricingModeAndBillDiscountTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_10_3_AnInclusiveShopChargesTheShelfPriceAndCarvesTheTaxOutOfIt()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax: true);
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "INCL", price: 118.00m, taxPercent: 18m);

        var completed = await CompleteAsync(fixture, [new(variantId, 1m)]);

        completed.Total.Should().Be(Money.FromDecimal(118.00m), "the shelf price already contains the tax");
        (await HeaderAsync(fixture, completed.SaleId)).Should().Be("1000000|0|180000|0|1180000");

        var receipt = await ReceiptAsync(fixture, completed.SaleId);
        receipt.Lines[0].LineTotal.Should().Be(Money.FromDecimal(118.00m), "the Amount column is what the line was charged");
        receipt.Subtotal.Should().Be(Money.FromDecimal(118.00m));
        receipt.TaxableValue.Should().Be(Money.FromDecimal(100.00m));
        receipt.Tax.Should().Be(Money.FromDecimal(18.00m));
        receipt.TaxBreakdown.Should().ContainSingle().Which.TaxableAmount.Should().Be(Money.FromDecimal(100.00m));
    }

    [Fact]
    public async Task FR_10_3_AnExclusiveShopAddsTheTaxOnTop()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax: false);
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "EXCL", price: 100.00m, taxPercent: 18m);

        var completed = await CompleteAsync(fixture, [new(variantId, 1m)]);

        completed.Total.Should().Be(Money.FromDecimal(118.00m));
        (await HeaderAsync(fixture, completed.SaleId)).Should().Be("1000000|0|180000|0|1180000");
    }

    [Theory]
    [InlineData(false, 100.00, 50.00)]
    [InlineData(true, 118.00, 59.00)]
    public async Task FR_3_17_ABillDiscountReducesTheTaxBaseAndTheReceiptAddsUp(
        bool pricesIncludeTax, decimal firstPrice, decimal secondPrice)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax);
        var first = await PricedVariantSeeder.SeedAsync(fixture, "BD1", firstPrice, taxPercent: 18m);
        var second = await PricedVariantSeeder.SeedAsync(fixture, "BD2", secondPrice, taxPercent: 18m);

        // A 10% bill discount, as an amount: 15.00 off 150.00 exclusive, 17.70 off 177.00 inclusive.
        var billDiscount = Money.FromDecimal((firstPrice + secondPrice) / 10m);

        var completed = await CompleteAsync(
            fixture, [new(first, 1m), new(second, 1m)], DiscountInput.OfAmount(billDiscount));

        // Either way the goods are worth 135.00 net after the discount, taxed at 18% = 24.30.
        // Taxing the pre-discount 150.00 instead would have charged 27.00.
        var receipt = await ReceiptAsync(fixture, completed.SaleId);
        receipt.Discount.Should().Be(billDiscount);
        receipt.TaxableValue.Should().Be(Money.FromDecimal(135.00m));
        receipt.Tax.Should().Be(Money.FromDecimal(24.30m));
        receipt.Total.Should().Be(Money.FromDecimal(159.30m));

        // The printed block reads as arithmetic: Sub total - Discount (- tax, inclusive) = Taxable
        // value, and Taxable value + Tax = TOTAL.
        (receipt.TaxableValue + receipt.Tax).Should().Be(receipt.Total);
        (receipt.Subtotal - receipt.Discount).Should().Be(
            pricesIncludeTax ? receipt.Total : receipt.TaxableValue);
        receipt.TaxBreakdown.Should().ContainSingle().Which.TaxableAmount.Should().Be(receipt.TaxableValue);
    }

    [Fact]
    public async Task FR_3_16_ALineDiscountIsNotSubtractedAgainInTheReceiptDiscountRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax: false);
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "LD", price: 100.00m, taxPercent: 0m);

        var completed = await CompleteAsync(
            fixture, [new(variantId, 3m, Discount: DiscountInput.OfRate(Percentage.FromPercent(10m)))]);

        completed.Total.Should().Be(Money.FromDecimal(270.00m));

        var receipt = await ReceiptAsync(fixture, completed.SaleId);
        receipt.Lines[0].LineTotal.Should().Be(Money.FromDecimal(270.00m), "the line prints net of its own discount");
        receipt.Subtotal.Should().Be(Money.FromDecimal(270.00m));
        receipt.Discount.Should().Be(Money.Zero, "the 30.00 line discount is already inside the line amount");
        receipt.TaxableValue.Should().Be(Money.FromDecimal(270.00m));
    }

    [Fact]
    public void BillDiscountSplit_IsRecomputableFromTheStoredColumnsAndSumsExactly()
    {
        var weights = new List<Money>
        {
            BillDiscountSplit.Weight(Money.FromDecimal(33.33m), Quantity.FromDecimal(3m, 1), Money.Zero),
            BillDiscountSplit.Weight(Money.FromDecimal(10m), Quantity.FromDecimal(1m, 1), Money.FromDecimal(1m)),
            BillDiscountSplit.Weight(Money.FromDecimal(0.07m), Quantity.FromDecimal(7m, 1), Money.Zero),
        };

        var shares = BillDiscountSplit.Allocate(Money.FromDecimal(10.01m), weights);

        (shares[0] + shares[1] + shares[2]).Should().Be(Money.FromDecimal(10.01m));
        BillDiscountSplit.Allocate(Money.FromDecimal(10.01m), weights).Should().Equal(shares, "the split is deterministic");
    }

    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture, IReadOnlyList<SaleLineRequest> lines, DiscountInput? billDiscount = null)
    {
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines, billDiscount);
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var shiftId = await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

        var completed = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, SoldAt, lines, [new TenderRequest(TenderTypes.Cash, quote.Total)], BillDiscount: billDiscount));

        completed.Total.Should().Be(quote.Total, "the quote on the screen is what gets charged");
        return completed;
    }

    private static Task<string?> HeaderAsync(SaleFixture fixture, long saleId) =>
        fixture.ScalarAsync(
            "SELECT subtotal || '|' || bill_discount || '|' || tax || '|' || rounding || '|' || total "
            + "FROM sale WHERE id = " + saleId + ";");

    private static async Task<Counterpoint.Application.Abstractions.Devices.SaleReceipt> ReceiptAsync(
        SaleFixture fixture, long saleId) =>
        (await fixture.Resolve<ISaleReceiptLookup>().FindReceiptAsync(saleId))!;
}
