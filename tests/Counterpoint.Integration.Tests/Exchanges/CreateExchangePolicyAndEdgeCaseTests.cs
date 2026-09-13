using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Exchanges;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Exchanges;

/// <summary>
/// Everything about exchanges (task P2-T04) that <c>CreateExchangeTests</c>'s own arithmetic
/// suite does not already cover: the return-policy override paths, the duplicate-line and
/// refund-method guards, AC-06's cumulative over-return guard reached through
/// <c>ReturnPricer</c>, the hash chains of both documents it writes, a DAMAGED disposition line,
/// an open-item replacement, and the negative-stock policy on the replacement side.
/// </summary>
public sealed class CreateExchangePolicyAndEdgeCaseTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ExchangedAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    // Ninety days before ExchangedAt - well outside the default 14-day return window (Q-03).
    private static readonly DateTimeOffset SoldAtWayBack = new(2026, 6, 1, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_5_6_AnExchangeAgainstABillOutsideTheReturnWindowIsRefusedWithoutAnOverrideAndAllowedWithOne()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAtWayBack, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var withoutOverride = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: []));

        var exception = await withoutOverride.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
        exception.Which.Action.Should().Be(ReturnPolicyAuditActions.ReturnWindowExceeded);

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0, "a refused exchange writes nothing");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE id != " + sale.SaleId + ";")).Should().Be(0);

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.ReturnWindowExceeded, "Owner approved, valued customer.",
            SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword));

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            ReturnWindowOverride: token));

        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(1);
    }

    [Fact]
    public async Task AC_05_ANonReturnableItemInAnExchangeIsRefusedWithoutAnOverrideAndAllowedWithOne()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        await fixture.ExecuteAsync(
            "UPDATE product SET non_returnable = 1 WHERE id = "
            + "(SELECT product_id FROM product_variant WHERE id = " + variantId + ");");

        var withoutOverride = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Changed mind")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: []));

        var exception = await withoutOverride.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
        exception.Which.Action.Should().Be(ReturnPolicyAuditActions.NonReturnableOverride);

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0);

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.NonReturnableOverride, "Customer insists.",
            SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword));

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Changed mind")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            NonReturnableOverride: token));

        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));
    }

    [Fact]
    public async Task Q_03_AnExchangeWithoutTheBillNumberPresentedIsRefusedWithoutAnOverrideAndAllowedWithOne()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // ReceiptRequired is true by default (Q-03).
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var withoutOverride = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "No receipt")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            BillReferencePresented: false));

        var exception = await withoutOverride.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Action.Should().Be(ReturnPolicyAuditActions.ReceiptNotPresented);

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0);

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.ReceiptNotPresented, "Bill lost, verified by phone.",
            SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword));

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "No receipt")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            BillReferencePresented: false,
            ReceiptRequirementOverride: token));

        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));
    }

    [Fact]
    public async Task FR_5_13_ACashSurplusAboveTheConfiguredLimitIsRefusedWithoutAnOverrideAndAllowedWithOne()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { CashRefundLimit = Money.FromDecimal(10.00m) } });

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Sell 5 (62.50); return 3 (37.50) for 1 replacement (12.50): a 25.00 cash surplus, above
        // the 10.00 limit just configured.
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var withoutOverride = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(3m, saleLineId), ReturnDisposition.Sellable, "Too many")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            ExcessRefundMethod: RefundMethod.Cash));

        var exception = await withoutOverride.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Action.Should().Be(ReturnPolicyAuditActions.CashRefundLimitExceeded);

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0);

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            ReturnPolicyAuditActions.CashRefundLimitExceeded, "Owner approved.",
            SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword));

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(3m, saleLineId), ReturnDisposition.Sellable, "Too many")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            ExcessRefundMethod: RefundMethod.Cash,
            CashRefundLimitOverride: token));

        created.RefundPaid.Should().Be(Money.FromDecimal(25.00m));
    }

    [Fact]
    public async Task ADuplicateSaleLineIdInTheReturnLinesIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var attempt = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [
                new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "First"),
                new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Same line again"),
            ],
            [new SaleLineRequest(variantId, 2m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(25.00m))]));

        (await attempt.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*appears twice*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0, "a rejected request writes nothing");
    }

    [Theory]
    [InlineData(RefundMethod.CreditNote)]
    [InlineData(RefundMethod.Exchange)]
    public async Task AnExcessRefundMethodOtherThanCashOrCardIsRefused(RefundMethod excessRefundMethod)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var attempt = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(3m, saleLineId), ReturnDisposition.Sellable, "Too many")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: [],
            ExcessRefundMethod: excessRefundMethod));

        await attempt.Should().ThrowAsync<InvalidOperationException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0);
    }

    [Fact]
    public async Task AC_06_CumulativeOverReturnAcrossAStandaloneReturnAndAnExchangeIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Sell 3. A standalone return already takes back 2 of them.
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 3m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Sellable, "Too many")],
            RefundMethod.Cash));

        // Only 1 remains returnable. An exchange asking to return 2 more against the same line
        // is refused - the guard is cumulative across documents, and there is no override for it
        // anywhere (AC-06, ReturnPolicy's own remarks) - reached here through ReturnPricer, the
        // exact function a standalone return itself calls.
        var overReturn = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 2m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(25.00m))]));

        var exception = await overReturn.Should().ThrowAsync<ReturnNotEligibleException>();
        exception.Which.Eligibility.Should().BeOfType<ReturnEligibility.Denied>();
        exception.Which.Action.Should().BeNull("AC-06 never offers an override to ask for");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(1, "only the standalone return committed");

        // Exactly what remains (1 more) is still allowed through the exchange.
        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: []));

        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));

        var qtyReturned = await fixture.ScalarAsync("SELECT qty_returned FROM sale_line WHERE id = " + saleLineId + ";");
        qtyReturned.Should().Be("30000", "all 3 sold, and not one thousandth more");
    }

    [Fact]
    public async Task DamagedDispositionWithinAnExchangePostsNoReturnInMovementButStillCreditsTheSettlement()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");
        var qtyOnHandAfterSale = await fixture.CountAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(2m, saleLineId), ReturnDisposition.Damaged, "Damaged in transit")],
            [new SaleLineRequest(variantId, 1m)],
            DifferenceTenders: []));

        // Damaged goods are not restocked - the sellable balance moves only by the replacement
        // going out, never by the returned pieces coming back.
        var qtyOnHandAfter = await fixture.CountAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        (qtyOnHandAfterSale - qtyOnHandAfter).Should().Be(10000, "1 unit sold out as the replacement, none restocked");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'RETURN' AND movement_type = 'RETURN_IN';"))
            .Should().Be(0, "a DAMAGED line posts no stock movement at all, exchange or not");

        // The return's value (25.00, at the original price) still fully funds the settlement even
        // though nothing physically came back to the shelf - disposition changes stock, never money.
        created.ReturnValue.Should().Be(Money.FromDecimal(25.00m));
        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m), "as much of the 25.00 as the 12.50 replacement can take");
        created.RefundPaid.Should().Be(Money.FromDecimal(12.50m), "the rest paid back for real");

        var disposition = await fixture.ScalarAsync(
            "SELECT disposition FROM sale_return_line WHERE sale_return_id = " + created.SaleReturnId + ";");
        disposition.Should().Be("DAMAGED");
    }

    [Fact]
    public async Task AnOpenItemReplacementLinePricesAndSettlesWithoutAnyCatalogueStock()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);
        var uomId = await fixture.CountAsync("SELECT id FROM uom ORDER BY id LIMIT 1;");

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong item")],
            [new SaleLineRequest(null, 1m, uomId, "Cut to length, one-off", Money.FromDecimal(20.00m))],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(7.50m))]));

        created.ReturnValue.Should().Be(Money.FromDecimal(12.50m));
        created.ReplacementTotal.Should().Be(Money.FromDecimal(20.00m));
        created.CreditApplied.Should().Be(Money.FromDecimal(12.50m));
        created.AmountCollected.Should().Be(Money.FromDecimal(7.50m));

        // No catalogue variant, so no stock movement at all for the replacement side.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = " + created.SaleId + ";"))
            .Should().Be(0, "an open item posts no stock movement (SRS FR-2.8)");

        (await fixture.ScalarAsync(
            "SELECT product_variant_id FROM sale_line WHERE sale_id = " + created.SaleId + ";"))
            .Should().BeNull("the replacement line is an open item - no catalogue variant to point at");
        (await fixture.ScalarAsync(
            "SELECT description FROM sale_line WHERE sale_id = " + created.SaleId + ";"))
            .Should().Be("Cut to length, one-off");
    }

    [Fact]
    public async Task ANegativeStockPolicyOfBlockRefusesAReplacementThatWouldTakeStockBelowZero()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { NegativeStock = NegativeStockPolicy.Block } });

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Seeded: 100 on hand. Selling 5 and returning 1 back in leaves 96 - asking for 500 more
        // as the replacement would take it well below zero, and the shop's policy blocks that.
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var attempt = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 500m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(6225.00m))]));

        (await attempt.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*below zero*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0, "a blocked exchange writes nothing");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE id != " + sale.SaleId + ";")).Should().Be(0);
    }

    [Fact]
    public async Task ANegativeStockPolicyOfBlockRefusesTwoReplacementLinesOfTheSameVariantThatTogetherWouldTakeStockBelowZero()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        await fixture.Resolve<ISettings>().UpdateAsync(
            s => s with { Policy = s.Policy with { NegativeStock = NegativeStockPolicy.Block } });

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Seeded: 100 on hand. Selling 5 and returning 1 back in leaves 96. Two replacement lines
        // of 60 each, of the very same variant, neither exceeds 96 on its own - the bug this test
        // guards against checked each line against the raw catalogue figure independently and let
        // both through, taking stock to -24. Checked cumulatively (as CompleteSaleHandler's own
        // P1-T09 fix checks repeated lines within one bill), the second line sees only 36 left
        // (96 - 60 already claimed by the first) and is refused.
        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var attempt = () => fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [
                new SaleLineRequest(variantId, 60m),
                new SaleLineRequest(variantId, 60m),
            ],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(1500.00m))]));

        (await attempt.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*below zero*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;")).Should().Be(0, "a blocked exchange writes nothing");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE id != " + sale.SaleId + ";")).Should().Be(0);
    }

    [Fact]
    public async Task BothDocumentsAnExchangeWritesHaveCorrectVerifiableHashChainsIncludingTheCrossLink()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var variantId = await SeededVariantIdAsync(fixture);
        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var sale = await CompleteAsync(fixture, variantId, userId, shiftId, SoldAt, quantity: 5m);
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            sale.SaleId,
            userId,
            shiftId,
            ExchangedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Wrong size")],
            [new SaleLineRequest(variantId, 3m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(25.00m))]));

        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                using var context = unitOfWork.CreateDbContext();

                var sales = await context.Set<Sale>().OrderBy(row => row.Id).ToListAsync(token);
                var returns = await context.Set<SaleReturn>().OrderBy(row => row.Id).ToListAsync(token);

                // Two bills now exist - the original sale and the exchange's own replacement sale
                // - one return - and every one of them verifies against its own predecessor.
                sales.Should().HaveCount(2);
                returns.Should().ContainSingle();

                var previousSaleHash = HashChain.GenesisHash;
                foreach (var row in sales)
                {
                    row.PrevHash.Should().Be(previousSaleHash, "the sale chain is walked in id order, one table, one chain");
                    SaleHashChain.Verify(row).Should().BeTrue();
                    previousSaleHash = row.RowHash;
                }

                var previousReturnHash = HashChain.GenesisHash;
                foreach (var row in returns)
                {
                    row.PrevHash.Should().Be(previousReturnHash);
                    SaleReturnHashChain.Verify(row).Should().BeTrue();
                    previousReturnHash = row.RowHash;
                }

                // The cross-link itself is chained into the return's own row hash - tampering
                // with which sale an exchange claims to be settled against would be detected the
                // same way tampering with its total_refund would be.
                var exchangeReturn = returns.Single();
                exchangeReturn.ExchangeSaleId.Should().Be(created.SaleId);
                exchangeReturn.OriginalSaleId.Should().Be(sale.SaleId);

                var tamperedCopy = new SaleReturn
                {
                    Id = exchangeReturn.Id,
                    ReturnNo = exchangeReturn.ReturnNo,
                    ReturnedAt = exchangeReturn.ReturnedAt,
                    BusinessDate = exchangeReturn.BusinessDate,
                    OriginalSaleId = exchangeReturn.OriginalSaleId,
                    ExchangeSaleId = 999_999_999,
                    CustomerId = exchangeReturn.CustomerId,
                    UserId = exchangeReturn.UserId,
                    ShiftId = exchangeReturn.ShiftId,
                    Subtotal = exchangeReturn.Subtotal,
                    Tax = exchangeReturn.Tax,
                    RestockingFee = exchangeReturn.RestockingFee,
                    TotalRefund = exchangeReturn.TotalRefund,
                    RefundMethod = exchangeReturn.RefundMethod,
                    AuthorisedBy = exchangeReturn.AuthorisedBy,
                    Reason = exchangeReturn.Reason,
                    PrevHash = exchangeReturn.PrevHash,
                    RowHash = exchangeReturn.RowHash,
                };

                SaleReturnHashChain.Verify(tamperedCopy).Should().BeFalse(
                    "exchange_sale_id is part of the canonical form (SaleReturnHashChain), so " +
                    "forging which sale settled this return would be caught");
            });
    }

    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture, long variantId, long userId, long shiftId, DateTimeOffset soldAt, decimal quantity)
    {
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                userId,
                shiftId,
                soldAt,
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
