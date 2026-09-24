using System;
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
/// Every tender that settles a bill has to be one the till can actually collect - on a plain sale
/// and on an exchange's difference alike (SRS FR-3.24, FR-5 store credit).
/// </summary>
public sealed class ExchangeTenderTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ExchangedAt = new(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task ACreditNoteTenderedForAnExchangeDifferenceIsActuallySpent()
    {
        await using var fixture = await ReadyAsync();
        var cheap = await PricedVariantSeeder.SeedAsync(fixture, "EXC", 40.00m, taxPercent: 0m);
        var dear = await PricedVariantSeeder.SeedAsync(fixture, "EXD", 100.00m, taxPercent: 0m);

        // A 40.00 credit note, from an earlier, unrelated return.
        var first = await SellAsync(fixture, cheap);
        var note = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            first.SaleId, await UserIdAsync(fixture), await ShiftIdAsync(fixture), ReturnedAt,
            [await ReturnLineAsync(fixture, first.SaleId)], RefundMethod.CreditNote));

        // Exchange a 40.00 item for a 100.00 one: 60.00 owed, 40.00 of it on the credit note.
        var second = await SellAsync(fixture, cheap);
        await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            second.SaleId,
            await UserIdAsync(fixture),
            await ShiftIdAsync(fixture),
            ExchangedAt,
            [await ReturnLineAsync(fixture, second.SaleId)],
            [new SaleLineRequest(dear, 1m)],
            [
                new TenderRequest(TenderTypes.CreditNote, Money.FromDecimal(40.00m), note.CreditNoteNumber),
                new TenderRequest(TenderTypes.Cash, Money.FromDecimal(20.00m)),
            ]));

        (await fixture.ScalarAsync("SELECT amount_remaining || '|' || status FROM credit_note WHERE number = '" + note.CreditNoteNumber + "';"))
            .Should().Be("0|SPENT", "a CREDIT_NOTE payment with no redemption behind it would leave the note spendable again");
        (await fixture.CountAsync("SELECT COUNT(*) FROM credit_note_redemption;")).Should().Be(1);
    }

    [Fact]
    public async Task AnOnAccountTenderIsRefusedWhileThereIsNoCustomerAccountToCharge()
    {
        await using var fixture = await ReadyAsync();
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "ONACC", 40.00m, taxPercent: 0m);

        var sell = async () => await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await UserIdAsync(fixture), await ShiftIdAsync(fixture), SoldAt,
            [new SaleLineRequest(variantId, 1m)], [new TenderRequest("ON_ACCOUNT", Money.FromDecimal(40.00m))]));

        (await sell.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*ON_ACCOUNT*");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(0);
    }

    private static async Task<SaleFixture> ReadyAsync()
    {
        var fixture = await SaleFixture.CreateSignedInAsync();
        var numbering = fixture.Resolve<INumberSequenceConfiguration>();
        await numbering.ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);
        await numbering.ConfigureAsync("CREDIT_NOTE", "CN-", "{prefix}{yyyy}-{n:000000}", 1);
        return fixture;
    }

    private static async Task<CompletedSale> SellAsync(SaleFixture fixture, long variantId) =>
        await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await UserIdAsync(fixture), await ShiftIdAsync(fixture), SoldAt,
            [new SaleLineRequest(variantId, 1m)], [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(40.00m))]));

    private static async Task<ReturnLineRequest> ReturnLineAsync(SaleFixture fixture, long saleId)
    {
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + saleId + ";");
        return new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Test");
    }

    private static Task<long> UserIdAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static Task<long> ShiftIdAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
