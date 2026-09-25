using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Returns;

/// <summary>
/// What a linked return pays back, and which day it is filed under (SRS FR-5, AC-03, FR-8.8).
/// </summary>
public sealed class ReturnSettlementTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_03_AReturnFromADiscountedBillRefundsWhatWasPaidNotThePreDiscountPrice()
    {
        await using var fixture = await ReadyAsync();
        var first = await PricedVariantSeeder.SeedAsync(fixture, "RD1", 100.00m, taxPercent: 18m);
        var second = await PricedVariantSeeder.SeedAsync(fixture, "RD2", 50.00m, taxPercent: 18m);

        // 150.00 of goods, 15.00 off the bill: the first line bears 10.00 of it, the second 5.00.
        // Paid: 90.00 + 16.20 tax and 45.00 + 8.10 tax = 159.30.
        var sale = await SellAsync(fixture, [new(first, 1m), new(second, 1m)], DiscountInput.OfAmount(Money.FromDecimal(15.00m)));
        sale.Total.Should().Be(Money.FromDecimal(159.30m));

        var firstRefund = await ReturnAsync(fixture, sale.SaleId, lineNo: 1, quantity: 1m);
        firstRefund.TotalRefund.Should().Be(
            Money.FromDecimal(106.20m), "90.00 paid plus its 16.20 tax - not the 118.00 it was priced at before the discount");

        var secondRefund = await ReturnAsync(fixture, sale.SaleId, lineNo: 2, quantity: 1m);
        (firstRefund.TotalRefund + secondRefund.TotalRefund).Should().Be(
            sale.Total, "returning everything refunds exactly what the bill was paid");
    }

    [Fact]
    public async Task AC_06_ALineReturnedOneUnitAtATimeNeverRefundsMoreThanTheLine()
    {
        await using var fixture = await ReadyAsync();

        // 3 x 3.3367 = 10.0101, charged 10.01. A third of that is 3.3367, which rounds to 3.34
        // on its own - three separate returns priced independently would pay back 10.02.
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "THIRDS", 3.3367m, taxPercent: 0m);
        var sale = await SellAsync(fixture, [new(variantId, 3m)]);
        sale.Total.Should().Be(Money.FromDecimal(10.01m));

        var refunds = Money.Zero;
        for (var i = 0; i < 3; i++)
        {
            refunds += (await ReturnAsync(fixture, sale.SaleId, lineNo: 1, quantity: 1m)).TotalRefund;
        }

        refunds.Should().Be(Money.FromDecimal(10.01m));
    }

    [Fact]
    public async Task FR_8_8_AReturnIsFiledUnderTheDayItHappensNotTheDayOfTheOriginalSale()
    {
        await using var fixture = await ReadyAsync();
        await fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("CREDIT_NOTE", "CN-", "{prefix}{yyyy}-{n:000000}", 1);
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "DATED", 20.00m, taxPercent: 0m);
        var sale = await SellAsync(fixture, [new(variantId, 2m)]);

        var created = await ReturnAsync(fixture, sale.SaleId, lineNo: 1, quantity: 1m, RefundMethod.CreditNote);

        // The sale's day may already be closed and rolled up (P3-T03); a return filed under it
        // would never reach any day's figures.
        (await fixture.ScalarAsync("SELECT business_date FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be("2026-09-10");
        (await fixture.ScalarAsync("SELECT business_date FROM sale WHERE id = " + sale.SaleId + ";"))
            .Should().Be("2026-09-06");
    }

    private static async Task<SaleFixture> ReadyAsync()
    {
        var fixture = await SaleFixture.CreateSignedInAsync();
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax: false);
        await fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);
        return fixture;
    }

    private static async Task<CompletedSale> SellAsync(
        SaleFixture fixture, IReadOnlyList<SaleLineRequest> lines, DiscountInput? billDiscount = null)
    {
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines, billDiscount);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await UserIdAsync(fixture), await ShiftIdAsync(fixture), SoldAt, lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)], BillDiscount: billDiscount));
    }

    private static async Task<CreatedReturn> ReturnAsync(
        SaleFixture fixture, long saleId, int lineNo, decimal quantity, RefundMethod refundMethod = RefundMethod.Cash)
    {
        var saleLineId = await fixture.CountAsync(
            "SELECT id FROM sale_line WHERE sale_id = " + saleId + " AND line_no = " + lineNo + ";");

        return await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            saleId,
            await UserIdAsync(fixture),
            await ShiftIdAsync(fixture),
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(quantity, saleLineId), ReturnDisposition.Sellable, "Test")],
            refundMethod));
    }

    private static Task<long> UserIdAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static Task<long> ShiftIdAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
