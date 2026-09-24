using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// A cancellation voids a whole bill as if it never happened (SRS FR-3.34). These are the states
/// in which that is no longer true, and a return is the right document instead.
/// </summary>
public sealed class CancelSaleGuardTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset SameDayLater = new(2026, 9, 6, 17, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_3_34_ABillWithGoodsAlreadyReturnedCannotBeCancelled()
    {
        await using var fixture = await ReadyAsync();
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "CXR", 40.00m, taxPercent: 0m);
        var sale = await SellAsync(fixture, variantId, 2m, [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(80.00m))]);

        await ReturnOneAsync(fixture, sale.SaleId, RefundMethod.Cash);
        var stockBefore = await StockAsync(fixture, variantId);

        var cancel = () => fixture.Resolve<ICancelSale>().CancelAsync(new CancelSaleCommand(sale.SaleId, "Void", SameDayLater));

        (await cancel.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*already had goods returned*");
        (await StockAsync(fixture, variantId)).Should().Be(stockBefore, "the returned unit must not be restocked a second time");
        (await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + sale.SaleId + ";")).Should().Be("COMPLETED");
    }

    [Fact]
    public async Task FR_3_34_ABillPaidByCreditNoteCannotBeCancelled()
    {
        await using var fixture = await ReadyAsync();
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "CXN", 40.00m, taxPercent: 0m);
        var original = await SellAsync(fixture, variantId, 1m, [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(40.00m))]);
        var note = await ReturnOneAsync(fixture, original.SaleId, RefundMethod.CreditNote);

        var paidByNote = await SellAsync(
            fixture, variantId, 1m, [new TenderRequest(TenderTypes.CreditNote, Money.FromDecimal(40.00m), note.CreditNoteNumber)]);

        var cancel = () => fixture.Resolve<ICancelSale>().CancelAsync(new CancelSaleCommand(paidByNote.SaleId, "Void", SameDayLater));

        (await cancel.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*credit note*");
    }

    [Fact]
    public async Task FR_3_34_ABillFromAShiftAlreadyClosedCannotBeCancelled()
    {
        await using var fixture = await ReadyAsync(includeBackup: true);
        var variantId = await PricedVariantSeeder.SeedAsync(fixture, "CXS", 40.00m, taxPercent: 0m);
        var sale = await SellAsync(fixture, variantId, 1m, [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(40.00m))]);

        await fixture.Resolve<ICloseShift>().CloseAsync(new CloseShiftCommand(
            await ShiftIdAsync(fixture), await UserIdAsync(fixture), Money.FromDecimal(40.00m), SameDayLater.AddHours(-1), "Counted"));

        var cancel = () => fixture.Resolve<ICancelSale>().CancelAsync(new CancelSaleCommand(sale.SaleId, "Void", SameDayLater));

        (await cancel.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*already closed*");
    }

    private static async Task<SaleFixture> ReadyAsync(bool includeBackup = false)
    {
        // ICloseShift runs the on-close backup, so a test that closes a shift needs that wiring.
        var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: includeBackup);
        var numbering = fixture.Resolve<INumberSequenceConfiguration>();
        await numbering.ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);
        await numbering.ConfigureAsync("CREDIT_NOTE", "CN-", "{prefix}{yyyy}-{n:000000}", 1);
        return fixture;
    }

    private static async Task<CompletedSale> SellAsync(
        SaleFixture fixture, long variantId, decimal quantity, IReadOnlyList<TenderRequest> tenders) =>
        await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await UserIdAsync(fixture), await ShiftIdAsync(fixture), SoldAt, [new SaleLineRequest(variantId, quantity)], tenders));

    private static async Task<CreatedReturn> ReturnOneAsync(SaleFixture fixture, long saleId, RefundMethod refundMethod)
    {
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + saleId + ";");

        return await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            saleId,
            await UserIdAsync(fixture),
            await ShiftIdAsync(fixture),
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Test")],
            refundMethod));
    }

    private static Task<string?> StockAsync(SaleFixture fixture, long variantId) =>
        fixture.ScalarAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");

    private static Task<long> UserIdAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static Task<long> ShiftIdAsync(SaleFixture fixture) =>
        fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
