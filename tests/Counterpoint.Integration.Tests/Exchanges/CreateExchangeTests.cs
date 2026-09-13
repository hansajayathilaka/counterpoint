using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Exchanges;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Exchanges;

/// <summary>
/// Exchanges end to end: a return and a sale in one transaction, cross-linked, with the
/// difference settled once (SRS FR-5 exchange, AC-04, task P2-T04).
/// </summary>
public sealed class CreateExchangeTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ExchangedAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_04_AHigherPricedReplacementCollectsExactlyTheDifferenceInOneTransaction()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Seeded: 100 on hand @ 12.50. Sell 5 (62.50), then exchange 1 of them (12.50 back) for
        // 3 more of the same item (37.50 out) - a higher-priced replacement.
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");
        var qtyOnHandBefore = await fixture.CountAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 3m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(25.00m))]));

        created.ReturnValue.Should().Be(Money.FromDecimal(12.50m));
        created.ReplacementTotal.Should().Be(Money.FromDecimal(37.50m));
        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));
        created.AmountCollected.Should().Be(Money.FromDecimal(25.00m), "37.50 - 12.50 = 25.00, exactly the difference");
        created.Change.Should().Be(Money.Zero);
        created.RefundPaid.Should().Be(Money.Zero);

        // sale.total is the net amount actually owed, not the full replacement value - that is
        // what makes "collect tender for the difference" and "sum(payments) == sale.total" both
        // true at once (CreateExchangeHandler's own remarks).
        (await fixture.ScalarAsync("SELECT total FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("250000", "25.00, scaled x10 000 - the difference, not the full 37.50");
        (await fixture.ScalarAsync("SELECT bill_discount FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("125000", "12.50 credited from the return, scaled x10 000");
        (await fixture.ScalarAsync("SELECT subtotal FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("375000", "the full replacement merchandise value still shows in subtotal");

        (await fixture.ScalarAsync("SELECT SUM(amount) FROM payment WHERE sale_id = " + created.SaleId + ";"))
            .Should().Be("250000", "sum(payments) == sale.total exactly");

        // Cross-linked, both ways queryable.
        (await fixture.ScalarAsync("SELECT exchange_sale_id FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be(created.SaleId.ToString(CultureInfo.InvariantCulture));
        (await fixture.ScalarAsync("SELECT refund_method FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be("EXCHANGE");

        // No refund payment on the return - the whole return value went into the sale's credit.
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be(0);

        // Stock: +1 (returned back in) -3 (sold out) = net -2 on top of what stood after the sale.
        var qtyOnHandAfter = await fixture.CountAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        (qtyOnHandAfter - qtyOnHandBefore).Should().Be(-20000, "1 unit returned in, 3 sold out: net -2, scaled x10 000");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'RETURN' AND ref_doc_id = " + created.SaleReturnId + " AND movement_type = 'RETURN_IN';"))
            .Should().Be(1);
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = " + created.SaleId + " AND movement_type = 'SALE';"))
            .Should().Be(1);

        // One combined print job, not two.
        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_id = " + created.SaleId + " AND doc_type = 'SALE';"))
            .Should().Be(1);
        var printJob = await fixture.ScalarAsync(
            "SELECT LENGTH(payload) FROM print_job WHERE id = " + created.PrintJobId + ";");
        printJob.Should().NotBe("0");
    }

    [Fact]
    public async Task ALowerPricedReplacementRefundsExactlyTheSurplus()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Sell 5, then exchange 3 of them (37.50 back) for 1 more (12.50 out) - a lower-priced
        // replacement. The customer is owed the 25.00 surplus for real, in cash.
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(3m, saleLineId), ReturnDisposition.Sellable, "Too many")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: []));

        created.ReturnValue.Should().Be(Money.FromDecimal(37.50m));
        created.ReplacementTotal.Should().Be(Money.FromDecimal(12.50m));
        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));
        created.AmountCollected.Should().Be(Money.Zero);
        created.RefundPaid.Should().Be(Money.FromDecimal(25.00m));

        (await fixture.ScalarAsync("SELECT total FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("0", "fully covered by the return's own credit - nothing owed");
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_id = " + created.SaleId + ";"))
            .Should().Be(0, "nothing was tendered for a sale that owes nothing");

        (await fixture.ScalarAsync("SELECT SUM(amount) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be("-250000", "the 25.00 surplus refunded for real, negative as every refund payment is");

        (await fixture.ScalarAsync("SELECT refund_method FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be("EXCHANGE", "still an exchange even though part of it was paid back in cash");
    }

    [Fact]
    public async Task AnEvenExchangeCollectsNothingAndRefundsNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Sellable, "Wrong colour")],
            [new SaleLineRequest(variantId, 2m)],
            DifferenceTenders: []));

        created.AmountCollected.Should().Be(Money.Zero);
        created.RefundPaid.Should().Be(Money.Zero);
        created.CreditApplied.Should().Be(Money.FromDecimal(25.00m));

        (await fixture.ScalarAsync("SELECT total FROM sale WHERE id = " + created.SaleId + ";")).Should().Be("0");
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_id = " + created.SaleId + ";")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";")).Should().Be(0);
    }

    /// <summary>
    /// Daily totals reconcile: net sales (sum of sale.total across both documents' own sale rows)
    /// and refund payments both come out exactly right without any special-case netting -
    /// CreateExchangeHandler's own remarks on why this is true by construction.
    /// </summary>
    [Fact]
    public async Task DailyTotalsWithAnExchangeReconcileExactly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 3m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(25.00m))]));

        // Net cash the till actually took today from both documents together: the original
        // sale's 62.50, plus the exchange sale's own net 25.00 - never the exchange's full 37.50
        // replacement value counted as if it were an unrelated fresh sale.
        var netSalesTotal = await fixture.ScalarAsync("SELECT SUM(total) FROM sale WHERE status = 'COMPLETED';");
        netSalesTotal.Should().Be("875000", "62.50 (original) + 25.00 (exchange difference) = 87.50, scaled x10 000");

        var totalRefundPayments = await fixture.ScalarAsync(
            "SELECT COALESCE(SUM(amount), 0) FROM payment WHERE sale_return_id IS NOT NULL;");
        totalRefundPayments.Should().Be("0", "the exchange's return value was credited, not paid out - no refund payment exists to double count");

        var totalTenderedCash = await fixture.ScalarAsync(
            "SELECT SUM(amount) FROM payment WHERE sale_id IS NOT NULL AND tender_type = 'CASH';");
        totalTenderedCash.Should().Be("875000", "sum(payment) across both sale rows equals sum(sale.total) exactly");
    }

    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture, long variantId, long userId, long shiftId, decimal quantity)
    {
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                userId,
                shiftId,
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
