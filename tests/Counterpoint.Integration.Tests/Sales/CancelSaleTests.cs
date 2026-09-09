using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// Cancelling a completed bill: owner-only, same business day only, a reason required, stock
/// reversed exactly through <c>IStockLedger.PostAsync</c>, the bill number kept, and an audit row
/// written (P1-T10, SRS FR-3.34, CLAUDE.md invariants 3, 5, 8).
/// </summary>
public sealed class CancelSaleTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset SameDayLater = new(2026, 9, 6, 17, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset NextBusinessDay = new(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_3_34_CancellingReversesStockExactlyKeepsTheBillNumberAndWritesAnAuditRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);

        var qtyBefore = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");

        var completed = await CompleteBoltSaleAsync(fixture, 7m, SoldAt);

        var qtyAfterSale = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        qtyAfterSale.Should().NotBe(qtyBefore, "the sale itself must have moved the balance");

        var cancelled = await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, "Customer changed their mind", SameDayLater));

        cancelled.BillNo.Should().Be(completed.BillNo);
        cancelled.ReversedMovementCount.Should().Be(1, "one stock-posting line on this bill");

        var qtyAfterCancel = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        qtyAfterCancel.Should().Be(
            qtyBefore, "the compensating movement puts back exactly what the sale took out, never more, never less");

        // Never a delete, never a raw UPDATE: exactly the original outbound SALE movement and one
        // compensating inbound SALE movement against this bill (CLAUDE.md invariant 3).
        var saleMovements = await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = "
            + completed.SaleId + ";");
        saleMovements.Should().Be(2);

        var firstMovementQty = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = "
            + completed.SaleId + " ORDER BY id ASC LIMIT 1;");
        var secondMovementQty = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = "
            + completed.SaleId + " ORDER BY id DESC LIMIT 1;");
        firstMovementQty.Should().Be("-70000", "the original outbound movement");
        secondMovementQty.Should().Be("70000", "the compensating inbound, at the same magnitude");

        var billNo = await fixture.ScalarAsync("SELECT bill_no FROM sale WHERE id = " + completed.SaleId + ";");
        billNo.Should().Be(completed.BillNo, "a cancelled bill keeps its number (CLAUDE.md invariant 4)");

        var status = await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + completed.SaleId + ";");
        status.Should().Be("CANCELLED");

        var cancelledBy = await fixture.ScalarAsync("SELECT cancelled_by FROM sale WHERE id = " + completed.SaleId + ";");
        cancelledBy.Should().Be(fixture.Resolve<ISession>().CurrentUser!.Id.ToString(CultureInfo.InvariantCulture));

        var auditRow = await fixture.ScalarAsync(
            "SELECT action || '|' || entity_type || '|' || entity_id || '|' || reason FROM audit_log "
            + "WHERE action = 'SALE_CANCELLED';");
        auditRow.Should().Be(
            "SALE_CANCELLED|sale|" + completed.SaleId.ToString(CultureInfo.InvariantCulture)
            + "|Customer changed their mind");

        var printJobCount = await fixture.CountAsync(
            "SELECT COUNT(*) FROM print_job WHERE doc_id = " + completed.SaleId + ";");
        printJobCount.Should().Be(2, "the sale receipt and the cancellation slip, both queued, never printed inside the transaction");
    }

    [Fact]
    public async Task FR_4_TheMovingAverageCostIsRestoredExactlyAfterASaleThenCancelWithNoOtherMovementsBetween()
    {
        // The implementer's own claim (SaleStockReversal.UnitCost's remarks): posting the
        // reversal back in at the sale's own COGS snapshot restores stock_balance.cost_avg to
        // exactly what it was immediately before the sale, "to the scaled integer". Proved here
        // against a shelf whose average is not a round number, so a reversal that got the wrong
        // quantity or the wrong cost would visibly fail to land back on it.
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var variantId = await SeededVariantIdAsync(fixture);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;

        // Shelf starts at 100 @ 9.0000 (seeded). Receive 50 more @ 15.0000:
        // (100*9 + 50*15) / 150 = 11.0000 - the average this test calls "pre-sale".
        await fixture.Resolve<IStockLedger>().PostAsync(new StockPosting(
            variantId,
            "GRN",
            Quantity.FromDecimal(50m, await PieceUomIdAsync(fixture)),
            Money.FromDecimal(15.00m),
            "GRN",
            RefDocId: null,
            userId,
            SoldAt));

        var preSaleAvg = await fixture.ScalarAsync(
            "SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        preSaleAvg.Should().Be("110000");

        var preSaleQty = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");

        var completed = await CompleteBoltSaleAsync(fixture, 13m, SoldAt);

        var cancelled = await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, "Wrong item rung up", SameDayLater));
        cancelled.ReversedMovementCount.Should().Be(1);

        var postCancelAvg = await fixture.ScalarAsync(
            "SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        postCancelAvg.Should().Be(
            preSaleAvg,
            "a symmetric pair around one average - the same quantity out and back in, at the same "
            + "cost - must leave the moving-average cost exactly where it was, to the scaled "
            + "integer, not merely close to it");

        var postCancelQty = await fixture.ScalarAsync(
            "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId + ";");
        postCancelQty.Should().Be(preSaleQty);
    }

    [Fact]
    public async Task FR_3_34_CancellingTheSameBillTwiceIsRejectedByTheSecondAttempt()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var completed = await CompleteBoltSaleAsync(fixture, 1m, SoldAt);

        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, "First cancellation", SameDayLater));

        var act = async () => await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, "Second attempt", SameDayLater));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already cancelled*");

        // Exactly one compensating pair, not two - a second cancellation must not double-reverse
        // the stock the first one already put back (CLAUDE.md invariant 3, invariant 5's
        // database-level backstop is trg_sale_cancel_only_forward).
        var saleMovements = await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = "
            + completed.SaleId + ";");
        saleMovements.Should().Be(2);
    }

    [Fact]
    public async Task FR_3_34_CancellingWithoutAReasonIsRejected()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var completed = await CompleteBoltSaleAsync(fixture, 1m, SoldAt);

        var act = async () => await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, string.Empty, SameDayLater));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*needs a reason*");

        (await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + completed.SaleId + ";"))
            .Should().Be("COMPLETED", "the refusal must land before anything is touched");
    }

    [Fact]
    public async Task FR_3_34_CancellingOnALaterBusinessDayIsRejected()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var completed = await CompleteBoltSaleAsync(fixture, 1m, SoldAt);

        var act = async () => await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, "Too late", NextBusinessDay));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*same business day*");

        (await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + completed.SaleId + ";"))
            .Should().Be("COMPLETED");
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = "
            + completed.SaleId + ";")).Should().Be(1, "only the original outbound movement - no reversal was posted");
    }

    [Fact]
    public async Task FR_3_34_AC_17_CancellingByANonOwnerIsRefusedWithTheUiBypassedAndNothingIsWritten()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var completed = await CompleteBoltSaleAsync(fixture, 1m, SoldAt);

        // Hand the till to a cashier, exactly as CompleteSaleTests's own AC-17-shaped test does:
        // create the account, sign in as them, call ICancelSale directly - no window, no
        // viewmodel, no confirmation dialog anywhere near this call (SRS NFR-S2, AC-17).
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var act = async () => await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(completed.SaleId, "Cashier trying to cancel", SameDayLater));

        await act.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + completed.SaleId + ";"))
            .Should().Be("COMPLETED", "the refusal happens in front of the service - nothing was read, written or partly done");
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM stock_movement WHERE ref_doc_type = 'SALE' AND ref_doc_id = "
            + completed.SaleId + ";")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'SALE_CANCELLED';"))
            .Should().Be(0);
    }

    private static async Task<CompletedSale> CompleteBoltSaleAsync(
        SaleFixture fixture, decimal quantity, DateTimeOffset soldAt)
    {
        var variantId = await SeededVariantIdAsync(fixture);
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            soldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    private static async Task<long> PieceUomIdAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<Counterpoint.Application.Catalogue.IUomMaintenance>();
        var all = await uoms.ListAsync();

        foreach (var uom in all)
        {
            if (uom.Name == "Piece")
            {
                return uom.Id;
            }
        }

        throw new InvalidOperationException("The seeded 'Piece' unit was not found.");
    }

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
