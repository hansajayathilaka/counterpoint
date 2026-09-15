using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Cash;

/// <summary>
/// The expected-drawer calculation against real data - a real cash sale, a real cash refund and
/// real cash movements, so this is a test of <see cref="Counterpoint.Infrastructure.Cash.SqliteCashMovementReader"/>'s
/// own SQL as much as it is of <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator"/>
/// (task P3-T01 "Do this" #2, "Done when": "expected cash matches a hand-worked example including
/// refunds, cash in and cash out").
/// </summary>
public sealed class ExpectedCashServiceTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 10, 9, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset MovementAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_8_1_ExpectedCashMatchesAHandWorkedExampleIncludingARealSaleReturnAndCashMovements()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        // Opening float is zero (FirstRunSeeder). Top it up first, the same order a real shift
        // would: float, then trading.
        await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, user.Id, Money.FromDecimal(5000m), "Float top-up", MovementAt));

        // A real cash sale of 3 pieces at the seeded 12.50 each = 37.50.
        var variantId = await SeededVariantIdAsync(fixture);
        var lines = new List<SaleLineRequest> { new(variantId, 3m) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        var sale = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id, shiftId, SoldAt, lines, [new TenderRequest(TenderTypes.Cash, quote.Total)]));

        sale.Total.Should().Be(Money.FromDecimal(37.50m), "the hand-worked example depends on this exact figure");

        // A real cash refund of 1 piece at the same price = 12.50.
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var returned = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            user.Id,
            shiftId,
            ReturnedAt,
            [new ReturnLineRequest(
                saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Customer changed mind")],
            RefundMethod.Cash));

        returned.TotalRefund.Should().Be(Money.FromDecimal(12.50m));

        // A cash-out of 1000, still under the default 5000 threshold - no override needed.
        await fixture.Resolve<ICashMovementService>().RecordCashOutAsync(new RecordCashOutCommand(
            shiftId, user.Id, Money.FromDecimal(1000m), "Petty expense", MovementAt.AddMinutes(5)));

        var summary = await fixture.Resolve<IExpectedCashService>().CalculateAsync(shiftId);

        summary.OpeningFloat.Should().Be(Money.Zero, "FirstRunSeeder opens the seeded shift with no float");
        summary.CashSales.Should().Be(Money.FromDecimal(37.50m));
        summary.CashRefunds.Should().Be(Money.FromDecimal(12.50m));
        summary.CashIn.Should().Be(Money.FromDecimal(5000m));
        summary.CashOut.Should().Be(Money.FromDecimal(1000m));

        // The hand-worked figure: 0 + 37.50 - 12.50 + 5000 - 1000 = 4025.00.
        summary.ExpectedCash.Should().Be(Money.FromDecimal(4025.00m));

        // The single-place-formula risk note (task P3-T01 "Risks"): the service's own answer is
        // exactly what a direct call to the one calculator method produces from the same figures,
        // never a second, independently-arrived-at number.
        summary.ExpectedCash.Should().Be(ExpectedCashCalculator.Calculate(
            summary.OpeningFloat, summary.CashSales, summary.CashRefunds, summary.CashIn, summary.CashOut));
    }

    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");
}
