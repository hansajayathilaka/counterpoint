using System;
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

namespace Counterpoint.Integration.Tests.CreditNotes;

/// <summary>
/// Credit notes end to end: issued from a return, redeemed as a tender, looked up by number,
/// bounded by a guarded decrement, expired only at redemption, and reconciled against the raw
/// rows (SRS FR-5 store credit, FR-3 tender, task P2-T05).
/// </summary>
/// <remarks>
/// <para>
/// A credit note is issued from a return of a real catalogue line, never an open item -
/// <c>sale_return_line.product_variant_id</c> is <c>NOT NULL</c> in the schema
/// (<c>ReturnPricer.PricedReturnLine.ToNewSaleReturnLine</c> refuses an open-item return outright).
/// <see cref="CreateZeroTaxVariantAsync"/> creates one throwaway product and variant per desired
/// amount, priced at exactly that amount, taxed at the shop's own seeded "Exempt" 0% class - the
/// same technique <c>CreateReturnTests.SeedTaxedVariantAsync</c> uses for a non-zero rate. Selling
/// and fully returning exactly one unit is what lets these tests assert exact figures instead of
/// whatever a fixed 12.50 catalogue price and a fractional quantity would have produced.
/// </para>
/// <para>
/// A <em>redemption</em> sale, by contrast, is never returned, so it is always built from a plain
/// open item (<see cref="SaleLineRequest.OpenItemUnitPrice"/>) - cheaper to set up, and taxed at
/// the shop's own 0% default rate (Q-02) either way, so the amount tendered is exactly the amount
/// that reaches <see cref="ICreditNoteRedeemer.RedeemAsync"/>.
/// </para>
/// </remarks>
public sealed class CreditNoteTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset RedeemedAt = new(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task ACreditNoteIssuedFromAReturnHasTheCorrectAmountAndPrints()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var (creditNoteId, number, amount, created) = await IssueCreditNoteAsync(
            fixture, Money.FromDecimal(25.00m), userId, shiftId);

        amount.Should().Be(Money.FromDecimal(25.00m));
        created.CreditNoteId.Should().NotBeNull();
        created.CreditNoteNumber.Should().NotBeNullOrWhiteSpace();
        created.CreditNotePrintJobId.Should().NotBeNull(
            "the credit note document is a second, separate outbox row from the return receipt");

        var note = await fixture.Resolve<ICreditNoteQuery>().FindByNumberAsync(number);
        note.Should().NotBeNull();
        note!.AmountIssued.Should().Be(Money.FromDecimal(25.00m));
        note.AmountRemaining.Should().Be(Money.FromDecimal(25.00m), "a freshly issued note has never been spent");
        note.Status.Should().Be("ACTIVE");
        note.SaleReturnId.Should().Be(created.SaleReturnId);
        note.Id.Should().Be(creditNoteId);

        // Queued rather than printed inside the transaction (CLAUDE.md invariant 7), carrying a
        // non-empty rendered byte stream - the same shape CreateReturnTests proves for the return
        // receipt itself.
        var printJob = await fixture.ScalarAsync(
            "SELECT doc_type || '|' || doc_id || '|' || LENGTH(payload) FROM print_job WHERE id = "
            + created.CreditNotePrintJobId + ";");
        printJob.Should().StartWith("CREDIT_NOTE|" + creditNoteId + "|").And.NotEndWith("|0");
    }

    [Fact]
    public async Task PartialRedemptionLeavesTheCorrectRemainingBalance()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var (creditNoteId, number, _, _) = await IssueCreditNoteAsync(
            fixture, Money.FromDecimal(50.00m), userId, shiftId);

        var redemptionSale = await RedeemCreditNoteAsync(
            fixture, number, Money.FromDecimal(20.00m), RedeemedAt, userId, shiftId);

        var note = await fixture.Resolve<ICreditNoteQuery>().FindByNumberAsync(number);
        note!.AmountRemaining.Should().Be(Money.FromDecimal(30.00m));
        note.Status.Should().Be("ACTIVE", "20 of 50 spent is still well short of zero");

        (await fixture.ScalarAsync(
            "SELECT credit_note_id || '|' || sale_id || '|' || amount FROM credit_note_redemption "
            + "WHERE credit_note_id = " + creditNoteId + ";"))
            .Should().Be(creditNoteId + "|" + redemptionSale.SaleId + "|200000", "amount is stored scaled x10 000");
    }

    [Fact]
    public async Task AFullRedemptionMarksTheNoteSpent()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var (_, number, amount, _) = await IssueCreditNoteAsync(
            fixture, Money.FromDecimal(18.75m), userId, shiftId);

        await RedeemCreditNoteAsync(fixture, number, amount, RedeemedAt, userId, shiftId);

        var note = await fixture.Resolve<ICreditNoteQuery>().FindByNumberAsync(number);
        note!.AmountRemaining.Should().Be(Money.Zero);
        note.Status.Should().Be("SPENT", "the balance reached exactly zero in the same write");
    }

    [Fact]
    public async Task RedeemingMoreThanTheRemainingBalanceIsRejectedAndTheWholeSaleRollsBack()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var (_, number, _, _) = await IssueCreditNoteAsync(fixture, Money.FromDecimal(20.00m), userId, shiftId);

        var saleCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM sale;");
        var saleLineCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM sale_line;");
        var paymentCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM payment;");
        var redemptionCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM credit_note_redemption;");

        // 25.00 against a 20.00 balance - a single tender, equal to the bill total, so
        // TenderCalculator itself has no objection; the guard that refuses this lives inside the
        // sale transaction (task P2-T05's own risk note), not before it.
        var overRedeem = async () => await RedeemCreditNoteAsync(
            fixture, number, Money.FromDecimal(25.00m), RedeemedAt, userId, shiftId);

        var exception = await overRedeem.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(number).And.Contain("does not have").And.Contain("left to redeem");

        // Not a rounding artefact and not a partial write: the whole sale transaction the refused
        // tender was inside of rolled back completely, exactly like a refused bill balance check.
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(
            saleCountBefore, "a refused tender writes nothing - not even the sale row, same as CompleteSaleHandler's other refusals");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_line;")).Should().Be(saleLineCountBefore);
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment;")).Should().Be(paymentCountBefore);
        (await fixture.CountAsync("SELECT COUNT(*) FROM credit_note_redemption;")).Should().Be(redemptionCountBefore);

        var note = await fixture.Resolve<ICreditNoteQuery>().FindByNumberAsync(number);
        note!.AmountRemaining.Should().Be(
            Money.FromDecimal(20.00m), "the guarded decrement itself lives inside the same rolled-back transaction");
        note.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task AnExpiredCreditNoteIsRefusedAtRedemptionAndDoesNotWriteExpiredStatus()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Issued on 2026-09-10 (ReturnedAt), with a future expiry that has not yet passed at the
        // moment it is issued - expiry is never checked at issue time (task P2-T05 step 3).
        var expiry = new DateOnly(2026, 9, 11);
        var (_, number, _, _) = await IssueCreditNoteAsync(
            fixture, Money.FromDecimal(15.00m), userId, shiftId, expiresOn: expiry);

        var afterExpiry = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(5.5));

        var saleCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM sale;");

        // Well within the balance (10.00 of 15.00) - isolating the expiry refusal from the
        // over-redemption refusal proven by the test above.
        var attempt = async () => await RedeemCreditNoteAsync(
            fixture, number, Money.FromDecimal(10.00m), afterExpiry, userId, shiftId);

        var exception = await attempt.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(number).And.Contain("expired on").And.Contain("2026-09-11");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(saleCountBefore);
        (await fixture.CountAsync("SELECT COUNT(*) FROM credit_note_redemption;")).Should().Be(0);

        var note = await fixture.Resolve<ICreditNoteQuery>().FindByNumberAsync(number);

        // SqliteCreditNoteStore.RequireRedeemable deliberately does not write status = 'EXPIRED'
        // on the way out - the enclosing sale transaction is about to roll back on the exception
        // regardless, so a write here would never reach disk either way. Proven directly here,
        // not assumed: the row genuinely still reads ACTIVE, with its balance untouched.
        note!.Status.Should().Be("ACTIVE");
        note.AmountRemaining.Should().Be(Money.FromDecimal(15.00m));
    }

    [Fact]
    public async Task AFailureRedeemingASecondCreditNoteRollsBackTheFirstNotesAlreadyAppliedRedemption()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Note A: fully redeemable in one tender - the transaction will decrement it to zero and
        // flip it to SPENT before the second tender ever gets a chance to fail.
        var (_, numberA, amountA, _) = await IssueCreditNoteAsync(fixture, Money.FromDecimal(15.00m), userId, shiftId);

        var uomId = await SeededUomIdAsync(fixture);
        const string BogusNumber = "CN-2026-999999";

        var saleCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM sale;");
        var paymentCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM payment;");
        var redemptionCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM credit_note_redemption;");

        // One bill, two CREDIT_NOTE tenders: the first spends note A in full (inside the
        // transaction, uncommitted); the second names a note that does not exist and throws. This
        // is not the over-redemption case above - note A's own guard passes cleanly. The failure
        // this test is about is the *sale's* transaction rolling back a redemption that, taken on
        // its own, had already succeeded.
        var attempt = async () => await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            shiftId,
            RedeemedAt,
            [new SaleLineRequest(null, 1m, uomId, "Item settled by two credit notes", Money.FromDecimal(20.00m))],
            [
                new TenderRequest(TenderTypes.CreditNote, amountA, numberA),
                new TenderRequest(TenderTypes.CreditNote, Money.FromDecimal(5.00m), BogusNumber),
            ]));

        var exception = await attempt.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("does not exist");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(
            saleCountBefore, "the second tender's failure rolls back the whole transaction, including the first tender's own payment row");
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment;")).Should().Be(paymentCountBefore);
        (await fixture.CountAsync("SELECT COUNT(*) FROM credit_note_redemption;")).Should().Be(redemptionCountBefore);

        var note = await fixture.Resolve<ICreditNoteQuery>().FindByNumberAsync(numberA);
        note!.AmountRemaining.Should().Be(
            amountA,
            "note A's guarded decrement genuinely ran inside this same transaction before the second "
            + "tender failed - if it were not rolled back along with everything else, this would read "
            + "zero, not the original balance");
        note.Status.Should().Be("ACTIVE", "the write that would have flipped this to SPENT never committed");
    }

    [Fact]
    public async Task TotalOutstandingCreditReconcilesAgainstIssuedMinusRedeemedAHandWorkedExample()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        // Three notes: 10.00, 20.00, 30.00 issued (60.00 total). Redeem 4.00 off the first,
        // 20.00 off the second (spending it to exactly zero), 5.00 off the third:
        //   outstanding = (10.00 - 4.00) + (20.00 - 20.00) + (30.00 - 5.00)
        //              =        6.00     +        0.00     +      25.00
        //              = 31.00, and 31.00 == 60.00 issued - 29.00 redeemed.
        var (_, note1, _, _) = await IssueCreditNoteAsync(fixture, Money.FromDecimal(10.00m), userId, shiftId);
        var (_, note2, amount2, _) = await IssueCreditNoteAsync(fixture, Money.FromDecimal(20.00m), userId, shiftId);
        var (_, note3, _, _) = await IssueCreditNoteAsync(fixture, Money.FromDecimal(30.00m), userId, shiftId);

        await RedeemCreditNoteAsync(fixture, note1, Money.FromDecimal(4.00m), RedeemedAt, userId, shiftId);
        await RedeemCreditNoteAsync(fixture, note2, amount2, RedeemedAt.AddMinutes(1), userId, shiftId);
        await RedeemCreditNoteAsync(fixture, note3, Money.FromDecimal(5.00m), RedeemedAt.AddMinutes(2), userId, shiftId);

        var reconciliation = await fixture.Resolve<ICreditNoteQuery>().ReconcileAsync();

        reconciliation.TotalIssued.Should().Be(Money.FromDecimal(60.00m));
        reconciliation.TotalRedeemed.Should().Be(Money.FromDecimal(29.00m));
        reconciliation.TotalOutstanding.Should().Be(Money.FromDecimal(31.00m));
    }

    /// <summary>
    /// The property this whole task's risk note exists to guarantee, over dozens of notes and
    /// redemptions rather than one hand-picked example: outstanding credit, as
    /// <see cref="ICreditNoteQuery.ReconcileAsync"/> reports it, equals issued minus redeemed -
    /// recomputed independently, straight off <c>credit_note</c> and
    /// <c>credit_note_redemption</c> as stored, the same discipline
    /// <c>CreateReturnTests.ReturnTotalsReconcileSumOfReturnLinesMinusFeeEqualsRefundPayments</c>
    /// uses for a return's own totals. Every note is issued and (partially) redeemed through the
    /// real handlers, not written by hand - a defect in the guarded decrement, in
    /// <c>ReconcileAsync</c>'s SQL, or in the SPENT-status flip would all show up here.
    /// </summary>
    [Fact]
    public async Task TotalOutstandingCreditReconcilesAgainstIssuedMinusRedeemedAcrossManyNotesAndRedemptions()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);
        await SeedCreditNoteNumberSequenceAsync(fixture);

        var userId = await SeededUserIdAsync(fixture);
        var shiftId = await SeededShiftIdAsync(fixture);

        var random = new Random(ReconciliationSeed);

        var totalIssued = Money.Zero;
        var totalRedeemed = Money.Zero;
        var minutesElapsed = 0;

        for (var i = 0; i < NoteCount; i++)
        {
            var issueAmount = Money.FromDecimal(decimal.Round((decimal)(random.NextDouble() * 490d) + 10.00m, 2));
            var (_, number, amount, _) = await IssueCreditNoteAsync(fixture, issueAmount, userId, shiftId);
            totalIssued += amount;

            var remaining = amount;
            var redemptionAttempts = random.Next(0, 4); // 0..3 redemptions against this one note

            for (var r = 0; r < redemptionAttempts && remaining.IsPositive; r++)
            {
                // Roughly three in ten redemptions take the note to exactly zero, so SPENT is
                // exercised, not just partial balances.
                var redeemAmount = random.NextDouble() < 0.3
                    ? remaining
                    : Money.FromDecimal(decimal.Round(remaining.Amount * (decimal)random.NextDouble(), 2));

                if (!redeemAmount.IsPositive)
                {
                    continue;
                }

                minutesElapsed++;
                await RedeemCreditNoteAsync(
                    fixture, number, redeemAmount, RedeemedAt.AddMinutes(minutesElapsed), userId, shiftId);

                totalRedeemed += redeemAmount;
                remaining -= redeemAmount;
            }
        }

        var expectedOutstanding = totalIssued - totalRedeemed;

        // Recomputed straight from the stored rows - not trusting the identity by construction
        // (CreditNoteReconciliation's own remarks on why it carries all three figures separately).
        var storedIssued = Money.FromScaled(await fixture.CountAsync("SELECT COALESCE(SUM(amount_issued), 0) FROM credit_note;"));
        var storedRedeemed = Money.FromScaled(await fixture.CountAsync("SELECT COALESCE(SUM(amount), 0) FROM credit_note_redemption;"));
        var storedOutstanding = Money.FromScaled(await fixture.CountAsync("SELECT COALESCE(SUM(amount_remaining), 0) FROM credit_note;"));

        storedIssued.Should().Be(totalIssued);
        storedRedeemed.Should().Be(totalRedeemed);
        storedOutstanding.Should().Be(expectedOutstanding);

        var reconciliation = await fixture.Resolve<ICreditNoteQuery>().ReconcileAsync();
        reconciliation.TotalIssued.Should().Be(totalIssued);
        reconciliation.TotalRedeemed.Should().Be(totalRedeemed);
        reconciliation.TotalOutstanding.Should().Be(expectedOutstanding);

        // Not a vacuous run: at least one note was actually driven to zero and flipped SPENT, and
        // at least one redemption happened at all.
        totalRedeemed.IsPositive.Should().BeTrue("a run of 30 notes with up to 3 redemptions each that redeemed nothing would prove nothing");
        (await fixture.CountAsync("SELECT COUNT(*) FROM credit_note WHERE status = 'SPENT';"))
            .Should().BeGreaterThan(0, "the random redemptions must have exhausted at least one note to zero");
    }

    private const int ReconciliationSeed = 20_260_914;
    private const int NoteCount = 30;

    /// <summary>
    /// Issues a credit note for exactly <paramref name="amount"/>: a fresh, single-purpose
    /// catalogue variant priced at that amount (see the class remarks - an open item cannot be
    /// returned), sold for cash and immediately returned in full as
    /// <see cref="RefundMethod.CreditNote"/>.
    /// </summary>
    private static async Task<(long CreditNoteId, string Number, Money Amount, CreatedReturn Created)> IssueCreditNoteAsync(
        SaleFixture fixture, Money amount, long userId, long shiftId, DateOnly? expiresOn = null)
    {
        var variantId = await CreateZeroTaxVariantAsync(fixture, amount);

        var sale = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            shiftId,
            SoldAt,
            [new SaleLineRequest(variantId, 1m)],
            [new TenderRequest(TenderTypes.Cash, amount)]));

        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");

        var created = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            userId,
            shiftId,
            ReturnedAt,
            [new ReturnLineRequest(saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable, "Store credit")],
            RefundMethod.CreditNote,
            CreditNoteExpiresOn: expiresOn));

        return (created.CreditNoteId!.Value, created.CreditNoteNumber!, created.TotalRefund, created);
    }

    /// <summary>Redeems <paramref name="amount"/> off credit note <paramref name="number"/> as the sole tender of a fresh sale.</summary>
    /// <remarks>
    /// Built from an open item, unlike <see cref="IssueCreditNoteAsync"/>'s own sale - a
    /// redemption sale is never returned, so there is no <c>sale_return_line.product_variant_id
    /// NOT NULL</c> constraint to satisfy here, and an open item is simpler to price exactly.
    /// </remarks>
    private static async Task<CompletedSale> RedeemCreditNoteAsync(
        SaleFixture fixture, string number, Money amount, DateTimeOffset redeemedAt, long userId, long shiftId)
    {
        var uomId = await SeededUomIdAsync(fixture);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            shiftId,
            redeemedAt,
            [new SaleLineRequest(null, 1m, uomId, "Store credit redemption", amount)],
            [new TenderRequest(TenderTypes.CreditNote, amount, number)]));
    }

    /// <summary>
    /// One throwaway product and variant, priced at exactly <paramref name="price"/>, taxed at
    /// the shop's own seeded "Exempt" 0% class, with one unit of opening stock - see the class
    /// remarks for why a credit note's own source sale needs a real catalogue line rather than an
    /// open item.
    /// </summary>
    private static async Task<long> CreateZeroTaxVariantAsync(SaleFixture fixture, Money price)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var taxClassId = await context.Set<TaxClass>()
                .Where(row => row.Name == "Exempt")
                .Select(row => row.Id)
                .FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

            var product = new Product
            {
                Code = "CN-" + suffix,
                Name = "Credit note test item",
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClassId,
                CostAvg = Money.Zero,
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = null,
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
                Sku = "CN-" + suffix + "-A",
                Attributes = "{}",
                Price = price,
                Active = true,
                CreatedAt = SoldAt,
            };

            context.Add(variant);
            await context.SaveChangesAsync(token);

            await ledger.PostAsync(
                new StockPosting(
                    variant.Id,
                    "OPENING",
                    Quantity.FromDecimal(1m, uomId),
                    Money.Zero,
                    "OPENING",
                    RefDocId: null,
                    userId,
                    SoldAt),
                token).ConfigureAwait(false);

            return variant.Id;
        });
    }

    /// <summary>
    /// The real till always has this row before a return can exist - see
    /// <c>CreateReturnTests.SeedReturnNumberSequenceAsync</c>'s own remarks.
    /// </summary>
    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static Task<bool> SeedCreditNoteNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("CREDIT_NOTE", "CN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<long> SeededUomIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM uom ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
