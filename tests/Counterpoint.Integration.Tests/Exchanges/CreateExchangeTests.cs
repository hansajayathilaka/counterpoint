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
        created.AmountCollected.Should().Be(Money.FromDecimal(25.00m), "37.50 - 12.50 = 25.00, exactly the difference collected as a real tender");
        created.Change.Should().Be(Money.Zero);
        created.RefundPaid.Should().Be(Money.Zero);

        // sale.total is the full replacement value - the 12.50 credit settles as its own
        // EXCHANGE tender alongside the 25.00 cash, never a discount netted out of the total
        // (CreateExchangeHandler's own remarks, ExchangeTenderType0010).
        (await fixture.ScalarAsync("SELECT total FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("375000", "37.50, scaled x10 000 - the full replacement value");
        (await fixture.ScalarAsync("SELECT bill_discount FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("0", "the credit is a tender now, never a discount");
        (await fixture.ScalarAsync("SELECT subtotal FROM sale WHERE id = " + created.SaleId + ";"))
            .Should().Be("375000", "the full replacement merchandise value still shows in subtotal");

        (await fixture.ScalarAsync("SELECT SUM(amount) FROM payment WHERE sale_id = " + created.SaleId + ";"))
            .Should().Be("375000", "sum(payments) == sale.total exactly: 12.50 EXCHANGE + 25.00 CASH");

        // Cross-linked, both ways queryable.
        (await fixture.ScalarAsync("SELECT exchange_sale_id FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be(created.SaleId.ToString(CultureInfo.InvariantCulture));
        (await fixture.ScalarAsync("SELECT refund_method FROM sale_return WHERE id = " + created.SaleReturnId + ";"))
            .Should().Be("EXCHANGE");

        // The return's own half of the EXCHANGE double entry - the exact negative of the sale's
        // credit, never a real refund (nothing was paid out here).
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be(1);
        (await fixture.ScalarAsync("SELECT SUM(amount) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be("-125000", "12.50 credit, negative, matching the sale's own EXCHANGE tender exactly");

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
            .Should().Be("125000", "12.50, the full replacement value - fully settled by an EXCHANGE tender, nothing else owed");
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_id = " + created.SaleId + ";"))
            .Should().Be(1, "one EXCHANGE tender for the full 12.50 - no cash/card tender was needed");

        // The return carries both halves of the surplus: the 12.50 EXCHANGE credit (the sale's own
        // exact negative) and the real 25.00 cash refund - together the return's full 37.50 value.
        (await fixture.ScalarAsync("SELECT SUM(amount) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";"))
            .Should().Be("-375000", "-12.50 EXCHANGE credit + -25.00 real refund = -37.50, the return's full value negated");

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

        // The full replacement value (25.00) shows as sale.total; it is entirely settled by one
        // EXCHANGE tender on the sale and its exact negative on the return - even though nothing
        // real changes hands, both documents still keep their own sum(payment) == total identity.
        (await fixture.ScalarAsync("SELECT total FROM sale WHERE id = " + created.SaleId + ";")).Should().Be("250000");
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_id = " + created.SaleId + ";")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_return_id = " + created.SaleReturnId + ";")).Should().Be(1);
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

        // Both sale rows carry their full merchandise value now: the original sale's 62.50, and
        // the exchange sale's full 37.50 replacement value (not just the 25.00 difference) - a
        // "total sales" report counts every ticket's real size, exactly like an ordinary sale.
        var netSalesTotal = await fixture.ScalarAsync("SELECT SUM(total) FROM sale WHERE status = 'COMPLETED';");
        netSalesTotal.Should().Be("1000000", "62.50 (original) + 37.50 (exchange's full replacement value) = 100.00, scaled x10 000");

        // The only payment on the return side is the 12.50 EXCHANGE credit, negative - the sale's
        // own tender mirrored, never a real refund. A refund-only report still has to exclude
        // tender_type = 'EXCHANGE' explicitly; this is what it would see if it did not.
        var totalRefundPayments = await fixture.ScalarAsync(
            "SELECT COALESCE(SUM(amount), 0) FROM payment WHERE sale_return_id IS NOT NULL;");
        totalRefundPayments.Should().Be("-125000", "the return's own half of the EXCHANGE double entry, not a real refund");

        // Real cash only: the original sale's 62.50 plus the exchange's own 25.00 difference. The
        // EXCHANGE tender that settles the credit is never tender_type = 'CASH', so it never
        // inflates what the till actually took in today.
        var totalTenderedCash = await fixture.ScalarAsync(
            "SELECT SUM(amount) FROM payment WHERE sale_id IS NOT NULL AND tender_type = 'CASH';");
        totalTenderedCash.Should().Be("875000", "62.50 (original) + 25.00 (exchange difference) = 87.50, real cash only");
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
