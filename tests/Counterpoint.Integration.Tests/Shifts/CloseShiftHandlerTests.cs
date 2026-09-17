using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using Dapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Shifts;

/// <summary>
/// The Z report - shift close and rollups (task P3-T03, SRS FR-8.4, FR-8.5, FR-8.8, AC-11).
/// </summary>
public sealed class CloseShiftHandlerTests
{
    private const decimal TaxPercent = 10m;
    private const decimal UnitPrice = 100.00m;
    private const string Passphrase = "correct horse battery staple";

    // The same calendar day the fixture's seeded shift opens on (SaleFixture's FixedTimeProvider
    // starts at 2026-09-06T09:15+05:30): the rollup builder rebuilds for shift.business_date, so
    // every sale and return in these tests has to land on that same business date, or the rollup
    // this task builds and the rollup this task's own tests check would silently be talking about
    // two different days.
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ClosedAt = new(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_11_ZReportVarianceIsComputedCorrectlyAgainstADeliberatelyMiscountedDrawerAndTheShiftLocks()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // 2 pieces @ 100.00, 10% tax -> subtotal 200.00, tax 20.00, total 220.00, paid CASH.
        var sale = await CompleteAsync(fixture, variantId, quantity: 2m);
        sale.Total.Should().Be(Money.FromDecimal(220.00m), "the hand-worked example depends on this exact figure");

        // Expected cash: 0 opening float + 220.00 cash sales - 0 refunds + 0 in - 0 out = 220.00.
        // A deliberate miscount of 200.00 is 20.00 short - well inside the default note threshold
        // (500.00), so no note is required for this test to focus purely on the variance figure.
        var countedCash = Money.FromDecimal(200.00m);

        var closed = await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, countedCash, ClosedAt));

        closed.Report.ExpectedCash.Should().Be(Money.FromDecimal(220.00m));
        closed.Report.CountedCash.Should().Be(countedCash);
        closed.Report.Variance.Should().Be(Money.FromDecimal(-20.00m), "200.00 counted - 220.00 expected");
        closed.Report.ClosedByUserId.Should().Be(user.Id);

        var shiftRow = await fixture.ScalarAsync(
            "SELECT status || '|' || COALESCE(closed_at,'') || '|' || counted_cash || '|' "
            + "|| expected_cash || '|' || variance || '|' || closed_by FROM shift WHERE id = "
            + shiftId.ToString(CultureInfo.InvariantCulture) + ";");

        shiftRow.Should().Be(string.Join(
            '|',
            "CLOSED",
            ClosedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            Money.FromDecimal(200.00m).ToScaled().ToString(CultureInfo.InvariantCulture),
            Money.FromDecimal(220.00m).ToScaled().ToString(CultureInfo.InvariantCulture),
            Money.FromDecimal(-20.00m).ToScaled().ToString(CultureInfo.InvariantCulture),
            user.Id.ToString(CultureInfo.InvariantCulture)));

        // The Z report's own print_job row, queued inside the close transaction (unlike the X
        // report, which has no transaction to enqueue against).
        var printJobRow = await fixture.ScalarAsync(
            "SELECT doc_type || '|' || status || '|' || (length(payload) > 0) FROM print_job WHERE id = "
            + closed.PrintJobId.ToString(CultureInfo.InvariantCulture) + ";");
        printJobRow.Should().Be("Z_REPORT|PENDING|1");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'SHIFT_CLOSED' AND entity_id = "
            + shiftId.ToString(CultureInfo.InvariantCulture) + ";")).Should().Be(1);

        // FR-11.1: a backup is taken automatically on close.
        closed.BackupOutcome.Should().NotBeNull();
        closed.BackupOutcome!.Succeeded.Should().BeTrue(closed.BackupOutcome.FailureReason);
        Directory.GetFiles(fixture.SnapshotDirectory, "counterpoint-*.cpbk").Should().NotBeEmpty(
            "FR-11.1: a shift close takes a backup automatically");
    }

    [Fact]
    public async Task FR_8_4_AVarianceAboveTheThresholdWithoutANoteIsRefusedAndWithANoteSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        // No trading at all: expected cash is the opening float, zero. The default threshold is
        // 500.00 (SettingDefaults.Policy.ShiftCloseVarianceNoteThreshold), so a 600.00 miscount
        // is well over it.
        var countedCash = Money.FromDecimal(600.00m);

        var withoutNote = () => fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, countedCash, ClosedAt));

        await withoutNote.Should().ThrowAsync<ShiftCloseVarianceNoteRequiredException>();

        (await fixture.ScalarAsync("SELECT status FROM shift WHERE id = " + shiftId + ";"))
            .Should().Be("OPEN", "a refused close must leave the shift untouched");

        var closed = await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, countedCash, ClosedAt, "Till was over - counted twice"));

        closed.Report.Note.Should().Be("Till was over - counted twice");

        (await fixture.ScalarAsync("SELECT note FROM shift WHERE id = " + shiftId + ";"))
            .Should().Be("Till was over - counted twice");
    }

    [Fact]
    public async Task AC_11_AttemptingToPostASaleIntoAClosedShiftIsRejectedByTheDatabase()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, ClosedAt));

        var salesBefore = await fixture.CountAsync("SELECT COUNT(*) FROM sale;");

        var lines = new List<SaleLineRequest> { new(variantId, 1m) };
        var attempt = () => fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id, shiftId, ClosedAt.AddMinutes(1), lines, [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(110.00m))]));

        var thrown = await attempt.Should().ThrowAsync<Exception>();
        ExceptionChainMessages(thrown.Which).Should().Contain(
            message => message.Contains("cannot post into a closed shift", StringComparison.Ordinal),
            "the database's own trg_sale_shift_open trigger, not application logic, is what refuses this (SRS FR-8.5, AC-11)");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(
            salesBefore, "the rejected sale must leave no trace");
    }

    [Fact]
    public async Task FR_8_8_ReCloseIsRejectedAndNoDeleteOrRerunMethodExistsOnTheInterface()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, ClosedAt));

        // Through ICloseShift itself, a re-close attempt is refused even earlier than the
        // "already closed" check: closing cleared the session's own shift, so it is no longer
        // "the shift currently open on this till" at all (CloseShiftHandler.ClearShiftId).
        var reCloseThroughTheHandler = () => fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.FromDecimal(50.00m), ClosedAt.AddHours(1)));

        await reCloseThroughTheHandler.Should().ThrowAsync<NotAuthorisedException>(
            "the shift this session was trading in is gone the moment it closes");

        // The database-adjacent backstop that would catch a re-close attempt even if it somehow
        // got past the handler's own session check (a race between two concurrent close attempts
        // on this single-writer till, say, or a second session recovered onto the same shift id
        // before this one's close committed) - SRS FR-8.8, IShiftCloseWriter's own remarks.
        var reCloseThroughTheWriter = () => fixture.Resolve<IShiftCloseWriter>().CloseAsync(
            new ShiftClose(shiftId, ClosedAt.AddHours(1), Money.FromDecimal(50.00m), Money.Zero, Money.FromDecimal(50.00m), user.Id, null));

        await reCloseThroughTheWriter.Should().ThrowAsync<InvalidOperationException>(
            "SRS FR-8.8: a Z report can never be re-run");

        // Structural proof, not just behavioural: ICloseShift has exactly one method, and it is
        // not named to suggest a delete or a re-run.
        var methods = typeof(ICloseShift).GetMethods();
        methods.Should().ContainSingle(method => method.Name == nameof(ICloseShift.CloseAsync));
        methods.Should().NotContain(method =>
            method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || method.Name.Contains("Rerun", StringComparison.OrdinalIgnoreCase)
            || method.Name.Contains("Reopen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task P3_T03_RollupRowsMatchARecomputationFromRawDataExactly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SeedReturnNumberSequenceAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // Sale: 3 pieces @ 100.00, 10% tax -> subtotal 300.00, tax 30.00, total 330.00.
        var sale = await CompleteAsync(fixture, variantId, quantity: 3m);
        sale.Total.Should().Be(Money.FromDecimal(330.00m));

        // Return 1 of the 3 pieces: a third of a 300.00/30.00 line is 100.00 + 10.00 tax = 110.00.
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + sale.SaleId + ";");
        var returned = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            sale.SaleId,
            user.Id,
            shiftId,
            ReturnedAt,
            [new ReturnLineRequest(
                saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable,
                "Customer changed mind")],
            RefundMethod.Cash));
        returned.TotalRefund.Should().Be(Money.FromDecimal(110.00m));

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, ClosedAt));

        var businessDate = DateOnly.FromDateTime(SoldAt.Date);
        var check = await fixture.Resolve<IRollupConsistencyCheck>().CheckAsync(businessDate);

        check.RowExists.Should().BeTrue();
        check.Matches.Should().BeTrue("the stored rollup must equal a fresh recomputation from raw data");
        check.ProductMismatches.Should().BeEmpty(
            "every daily_product_summary row must also agree with a fresh recomputation");

        // Hand-worked cross-check against the stored row directly, independent of the checker
        // itself: gross (330.00 sale total, but the row's own Gross is the true pre-discount
        // figure - no discount was given here, so gross = subtotal = 300.00), net =
        // 300.00 - 0 discount - 100.00 return subtotal = 200.00, cogs = 3 * 60.00 - 1 * 60.00 =
        // 120.00 (CostAvg seeded at 60.00 per piece).
        var summaryRow = await fixture.ScalarAsync(
            "SELECT bill_count || '|' || gross || '|' || discount || '|' || tax || '|' || net || '|' "
            + "|| cogs || '|' || return_count || '|' || return_value FROM daily_sales_summary WHERE business_date = '"
            + businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "';");

        summaryRow.Should().Be(string.Join(
            '|',
            1,
            Money.FromDecimal(300.00m).ToScaled(),
            Money.FromDecimal(0.00m).ToScaled(),
            Money.FromDecimal(30.00m).ToScaled(),
            Money.FromDecimal(200.00m).ToScaled(),
            Money.FromDecimal(120.00m).ToScaled(),
            1,
            Money.FromDecimal(110.00m).ToScaled()));

        var productRow = await fixture.ScalarAsync(
            "SELECT qty_base || '|' || net || '|' || cogs FROM daily_product_summary WHERE business_date = '"
            + businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + "' AND product_variant_id = " + variantId.ToString(CultureInfo.InvariantCulture) + ";");

        productRow.Should().Be(string.Join(
            '|',
            Quantity.FromDecimal(2m, variantId).ToScaled(),
            Money.FromDecimal(200.00m).ToScaled(),
            Money.FromDecimal(120.00m).ToScaled()));
    }

    /// <summary>
    /// The rollup test above proves the day-header row and drives one product through
    /// <see cref="IRollupConsistencyCheck"/>. This test sells two different products, each with its
    /// own price and cost, in the same bill, and hand-verifies directly against the stored rows
    /// (independent of the checker) that each keeps its own exact row - proof that
    /// <see cref="DailyRollupCalculator"/> does not silently merge every variant's quantity, net and
    /// cost into one shared bucket instead of keying them by <c>product_variant_id</c>. The test
    /// below this one drives the same class of bug through <c>IRollupConsistencyCheck</c> itself.
    /// </summary>
    [Fact]
    public async Task P3_T03_RollupAttributesEachVariantToItsOwnRowIndependently()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        // Two products, deliberately different price and cost so a mix-up between them would
        // produce a figure that does not match either hand-worked expectation below.
        var variantA = await SeedTaxedVariantAsync(fixture, codeSuffix: "010", unitPrice: 100.00m, unitCost: 60.00m);
        var variantB = await SeedTaxedVariantAsync(fixture, codeSuffix: "020", unitPrice: 50.00m, unitCost: 40.00m);

        // A @ 2 x 100.00 = 200.00 subtotal, 20.00 tax; B @ 5 x 50.00 = 250.00 subtotal, 25.00 tax.
        // Bill: subtotal 450.00, tax 45.00, total 495.00.
        var sale = await CompleteAsync(
            fixture,
            [new SaleLineRequest(variantA, 2m), new SaleLineRequest(variantB, 5m)],
            SoldAt);
        sale.Total.Should().Be(Money.FromDecimal(495.00m), "the hand-worked example depends on this exact figure");

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, sale.Total, ClosedAt));

        var businessDate = DateOnly.FromDateTime(SoldAt.Date);
        var dateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var summaryRow = await fixture.ScalarAsync(
            "SELECT bill_count || '|' || gross || '|' || tax || '|' || net || '|' || cogs "
            + "FROM daily_sales_summary WHERE business_date = '" + dateText + "';");

        summaryRow.Should().Be(string.Join(
            '|',
            1,
            Money.FromDecimal(450.00m).ToScaled(),
            Money.FromDecimal(45.00m).ToScaled(),
            Money.FromDecimal(450.00m).ToScaled(),
            Money.FromDecimal(120.00m).ToScaled() + Money.FromDecimal(200.00m).ToScaled()));

        var productRowA = await fixture.ScalarAsync(
            "SELECT qty_base || '|' || net || '|' || cogs FROM daily_product_summary WHERE business_date = '"
            + dateText + "' AND product_variant_id = " + variantA.ToString(CultureInfo.InvariantCulture) + ";");

        productRowA.Should().Be(string.Join(
            '|',
            Quantity.FromDecimal(2m, variantA).ToScaled(),
            Money.FromDecimal(200.00m).ToScaled(),
            Money.FromDecimal(120.00m).ToScaled()),
            "variant A's own row must carry only variant A's figures");

        var productRowB = await fixture.ScalarAsync(
            "SELECT qty_base || '|' || net || '|' || cogs FROM daily_product_summary WHERE business_date = '"
            + dateText + "' AND product_variant_id = " + variantB.ToString(CultureInfo.InvariantCulture) + ";");

        productRowB.Should().Be(string.Join(
            '|',
            Quantity.FromDecimal(5m, variantB).ToScaled(),
            Money.FromDecimal(250.00m).ToScaled(),
            Money.FromDecimal(200.00m).ToScaled()),
            "variant B's own row must carry only variant B's figures, not merged with variant A's");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_product_summary WHERE business_date = '" + dateText + "';"))
            .Should().Be(2, "each variant sold must produce its own row, never a shared one");
    }

    /// <summary>
    /// <see cref="IRollupConsistencyCheck"/> must catch a per-product attribution bug, not just a
    /// day-header one (task P3-T03's own "Risks": protection against "rollups drifting from the raw
    /// data"). A bug that merges two variants' figures into the wrong row, or silently drops one,
    /// would sail through a check that only ever compares the single aggregate
    /// <c>daily_sales_summary</c> row. This test closes a shift (building correct rows for both
    /// tables), then directly corrupts one <c>daily_product_summary</c> row via raw SQL to simulate
    /// that drift, and proves the checker's own recomputation now disagrees and identifies exactly
    /// which variant and which stored/recomputed figures disagree.
    /// </summary>
    [Fact]
    public async Task P3_T03_RollupConsistencyCheckCatchesADriftedProductSummaryRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // 2 pieces @ 100.00, 10% tax -> subtotal 200.00, tax 20.00, total 220.00.
        var sale = await CompleteAsync(fixture, variantId, quantity: 2m);
        sale.Total.Should().Be(Money.FromDecimal(220.00m));

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, sale.Total, ClosedAt));

        var businessDate = DateOnly.FromDateTime(SoldAt.Date);
        var dateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var check = await fixture.Resolve<IRollupConsistencyCheck>().CheckAsync(businessDate);
        check.Matches.Should().BeTrue("the freshly built rollup must agree with itself before any drift is introduced");
        check.ProductMismatches.Should().BeEmpty();

        // Simulate a later correction silently corrupting this variant's own row, independent of
        // any raw sale/return data - the exact drift the day-header alone cannot reveal, since the
        // day-header total is unaffected by moving net between rows of the same day.
        await fixture.ExecuteAsync(
            "UPDATE daily_product_summary SET net = net + " + Money.FromDecimal(1.00m).ToScaled().ToString(CultureInfo.InvariantCulture)
            + " WHERE business_date = '" + dateText
            + "' AND product_variant_id = " + variantId.ToString(CultureInfo.InvariantCulture) + ";");

        var driftedCheck = await fixture.Resolve<IRollupConsistencyCheck>().CheckAsync(businessDate);

        driftedCheck.Matches.Should().BeFalse("a drifted daily_product_summary row must fail the check");
        driftedCheck.ProductMismatches.Should().ContainSingle()
            .Which.ProductVariantId.Should().Be(variantId);

        var mismatch = driftedCheck.ProductMismatches.Single();
        mismatch.Stored.Should().NotBeNull();
        mismatch.Recomputed.Should().NotBeNull();
        mismatch.Stored!.Net.Should().Be(Money.FromDecimal(200.00m) + Money.FromDecimal(1.00m),
            "the stored figure must reflect the raw UPDATE just made");
        mismatch.Recomputed!.Net.Should().Be(Money.FromDecimal(200.00m),
            "the recomputation is unaffected by the UPDATE and still reflects the raw sales data");
    }

    /// <summary>
    /// This till trades one calendar day at a time but can close and reopen a shift within that
    /// same day (SRS FR-8.1's own remarks: a shift, not a day, is the unit that closes). The
    /// rollup query groups by <c>business_date</c>, not by <c>shift_id</c>
    /// (<see cref="DailyRollupCalculator"/>'s own SQL), so a second shift closing on the same date
    /// must rebuild that date's row from every completed sale on it - both shifts' - never merely
    /// the second shift's own trading layered additively on top of what the first shift's close
    /// already wrote. This is the "full rebuild, not a merge" <c>SqliteDailyRollupBuilder</c>'s own
    /// remarks promise, proved end to end rather than read off the source.
    /// </summary>
    [Fact]
    public async Task P3_T03_ASecondShiftClosingOnTheSameBusinessDateRebuildsTheWholeDayNotJustItself()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var variantId = await SeedTaxedVariantAsync(fixture);

        var firstShiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var user = fixture.Resolve<ISession>().CurrentUser!;

        // Shift 1: 2 pieces @ 100.00, 10% tax -> subtotal 200.00, tax 20.00, total 220.00.
        var firstSale = await CompleteAsync(fixture, variantId, quantity: 2m);
        firstSale.Total.Should().Be(Money.FromDecimal(220.00m));

        var firstClose = ClosedAt;
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(firstShiftId, user.Id, firstSale.Total, firstClose));

        var businessDate = DateOnly.FromDateTime(SoldAt.Date);
        var dateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // After shift 1 alone, the rollup must show exactly shift 1's trading - the baseline the
        // second close below must correctly replace, not add to.
        (await fixture.ScalarAsync(
            "SELECT bill_count || '|' || net || '|' || cogs FROM daily_sales_summary WHERE business_date = '"
            + dateText + "';"))
            .Should().Be(string.Join(
                '|', 1, Money.FromDecimal(200.00m).ToScaled(), Money.FromDecimal(120.00m).ToScaled()));

        // A second shift, opened and closed later the same calendar day.
        var secondOpenedAt = firstClose.AddMinutes(30);
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, secondOpenedAt));
        var secondShiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        secondShiftId.Should().NotBe(firstShiftId, "this must be a genuinely different shift, not the same one reopened");

        var secondSoldAt = secondOpenedAt.AddMinutes(30);

        // Shift 2: 3 more pieces of the very same variant, same business date -> subtotal 300.00,
        // tax 30.00, total 330.00.
        var secondSale = await CompleteAsync(fixture, [new SaleLineRequest(variantId, 3m)], secondSoldAt);
        secondSale.Total.Should().Be(Money.FromDecimal(330.00m));

        var secondClose = secondSoldAt.AddHours(1);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(secondShiftId, user.Id, secondSale.Total, secondClose));

        // The day's rollup must now be the union of both shifts' trading: 5 pieces total, 2
        // bills, subtotal 500.00, tax 50.00, cogs 300.00 - never shift 2 alone (330.00/30.00/
        // 180.00, which an additive bug applied on top of shift 1's stale row could also produce
        // by coincidence, so the assertion below checks the exact combined figure, not just "grew").
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '" + dateText + "';"))
            .Should().Be(1, "one row per business date, never one per shift");

        (await fixture.ScalarAsync(
            "SELECT bill_count || '|' || gross || '|' || tax || '|' || net || '|' || cogs "
            + "FROM daily_sales_summary WHERE business_date = '" + dateText + "';"))
            .Should().Be(string.Join(
                '|',
                2,
                Money.FromDecimal(500.00m).ToScaled(),
                Money.FromDecimal(50.00m).ToScaled(),
                Money.FromDecimal(500.00m).ToScaled(),
                Money.FromDecimal(300.00m).ToScaled()),
                "the second close must rebuild the whole day from both shifts' completed sales, "
                + "not add its own figures on top of the first close's stale row");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_product_summary WHERE business_date = '" + dateText
            + "' AND product_variant_id = " + variantId.ToString(CultureInfo.InvariantCulture) + ";"))
            .Should().Be(1, "the same variant sold across two shifts on one day must still be one row, not two");

        (await fixture.ScalarAsync(
            "SELECT qty_base || '|' || net || '|' || cogs FROM daily_product_summary WHERE business_date = '"
            + dateText + "' AND product_variant_id = " + variantId.ToString(CultureInfo.InvariantCulture) + ";"))
            .Should().Be(string.Join(
                '|',
                Quantity.FromDecimal(5m, variantId).ToScaled(),
                Money.FromDecimal(500.00m).ToScaled(),
                Money.FromDecimal(300.00m).ToScaled()));
    }

    /// <summary>
    /// FR-11.1's "automatically on close" cuts both ways: it must run when the setting asks for
    /// it, and it must not even attempt to when the setting says no (CLAUDE.md invariant 7 reads
    /// both ways too - never silently doing something the owner turned off).
    /// </summary>
    [Fact]
    public async Task FR_11_1_ABackupIsSkippedRatherThanAttemptedWhenDisabledOnShiftClose()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        fixture.Resolve<IBackupPassphraseStore>().SetPassphrase(Passphrase);

        await fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot with
        {
            Backup = snapshot.Backup with { BackupOnShiftClose = false },
        });

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var closed = await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, ClosedAt));

        closed.BackupOutcome.Should().BeNull(
            "backup.on_shift_close is off, so IShiftCloseBackupTrigger must not even attempt one");

        (await fixture.ScalarAsync("SELECT status FROM shift WHERE id = " + shiftId + ";"))
            .Should().Be("CLOSED", "a disabled backup must never stop the shift itself from closing");

        (await fixture.CountAsync("SELECT COUNT(*) FROM backup_record;")).Should().Be(
            0, "no attempt at all should be recorded when the shift-close backup is switched off");

        Directory.GetFiles(fixture.SnapshotDirectory, "counterpoint-*.cpbk").Should().BeEmpty(
            "no snapshot file should be written when backup.on_shift_close is false");
    }

    /// <summary>
    /// CLAUDE.md invariant 7: "Never block the sale... Printer, scanner, drawer, scale, network
    /// and backup failures degrade with a warning." The shift close is already committed by the
    /// time <see cref="IShiftCloseBackupTrigger.RunIfEnabledAsync"/> runs
    /// (<see cref="CloseShiftHandler"/>'s own remarks); this proves a backup that actually fails -
    /// here, because no passphrase was ever set, so <c>SnapshotService.CreateSnapshotAsync</c>
    /// throws and <c>BackupOrchestrator.RunAsync</c> converts that into a failed outcome rather
    /// than letting it propagate - still leaves the shift closed and its audit row intact.
    /// </summary>
    [Fact]
    public async Task FR_11_1_AFailingBackupDoesNotBlockOrRollBackTheShiftClose()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        // Deliberately no IBackupPassphraseStore.SetPassphrase call: the backup step must fail.
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var closed = await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, ClosedAt));

        closed.BackupOutcome.Should().NotBeNull("a backup was attempted - it just failed");
        closed.BackupOutcome!.Succeeded.Should().BeFalse("no passphrase was ever set for this fixture");
        closed.BackupOutcome.FailureReason.Should().NotBeNullOrWhiteSpace(
            "a failed backup must say why, for the owner to act on");

        (await fixture.ScalarAsync("SELECT status FROM shift WHERE id = " + shiftId + ";"))
            .Should().Be("CLOSED", "a failed backup must never undo an already-committed shift close");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = 'SHIFT_CLOSED' AND entity_id = "
            + shiftId.ToString(CultureInfo.InvariantCulture) + ";"))
            .Should().Be(1, "the close's own audit row must survive a backup that fails after the transaction committed");

        Directory.GetFiles(fixture.SnapshotDirectory, "counterpoint-*.cpbk").Should().BeEmpty(
            "the failed attempt must not have left a snapshot file behind");
    }

    [Fact]
    public async Task P3_T03_VarianceHistoryIsRetainedAndReadableAcrossThirtySeededShifts()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var expectedVariances = new List<Money>();
        var when = SoldAt;

        for (var i = 0; i < 30; i++)
        {
            var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

            // No trading: expected cash is always the zero opening float, so counted cash is the
            // variance outright. Alternating over/short, both inside and outside the note
            // threshold, so every branch of "retained and reportable" is exercised.
            var countedCash = Money.FromDecimal(i % 2 == 0 ? 10m * i : -10m * i);
            var note = countedCash.Abs() > Money.FromDecimal(500m) ? "Seeded variance for history test" : null;

            var closed = await fixture.Resolve<ICloseShift>().CloseAsync(
                new CloseShiftCommand(shiftId, user.Id, countedCash, when, note));

            expectedVariances.Add(closed.Report.Variance);

            when = when.AddDays(1);

            if (i < 29)
            {
                await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, when));
            }
        }

        var connection = await fixture.OpenReadConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            var storedVariances = (await connection.QueryAsync<long>(
                "SELECT variance FROM shift WHERE status = 'CLOSED' ORDER BY id;")).ToList();

            storedVariances.Should().HaveCount(30);
            storedVariances.Should().BeEquivalentTo(
                expectedVariances.Select(v => v.ToScaled()),
                options => options.WithStrictOrdering(),
                "every one of the 30 closed shifts' variances must be retained exactly and in order (SRS FR-8.6)");
        }
    }

    [Fact]
    public async Task NobodySignedInCannotCloseAShift()
    {
        await using var fixture = await SaleFixture.CreateAsync(includeBackup: true);

        var attempt = () => fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(1, 1, Money.Zero, ClosedAt));

        await attempt.Should().ThrowAsync<NotAuthorisedException>();
    }

    /// <summary>
    /// There is only ever one open shift (C-01), so "own shift only" collapses to "the shift
    /// currently open on this till" - proved here by opening a second shift under a different
    /// user (via a raw repair-session close, since closing the first one is exactly the behaviour
    /// under test) and confirming the first cashier can no longer close it.
    /// </summary>
    [Fact]
    public async Task P3_T03_ACashierMayOnlyCloseTheShiftCurrentlyOpenOnThisTill()
    {
        const string Owner = "owner";
        const string OwnerPassword = "till2026";
        const string CashierUsername = "priya";
        const string CashierPassword = "counter1";

        await using var fixture = await SaleFixture.CreateAsync(includeBackup: true);

        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogInAsync(Owner, OwnerPassword);

        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand(CashierUsername, "Priya", CashierPassword, Role.Cashier));

        var firstShiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        // Closing the first shift directly by raw SQL, the same technique XReportServiceTests
        // uses to reach "a shift the signed-in cashier is not currently trading in" without
        // depending on the very close flow under test.
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(CashierUsername, CashierPassword)).Succeeded.Should().BeTrue();
        var cashier = fixture.Resolve<ISession>().CurrentUser!;

        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(cashier.Id, Money.Zero, ClosedAt));

        var attempt = () => fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(firstShiftId, cashier.Id, Money.Zero, ClosedAt));

        await attempt.Should().ThrowAsync<NotAuthorisedException>(
            "a shift can only be closed by the session currently trading in it");
    }

    private static IEnumerable<string> ExceptionChainMessages(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static Task<CompletedSale> CompleteAsync(SaleFixture fixture, long variantId, decimal quantity) =>
        CompleteAsync(fixture, [new SaleLineRequest(variantId, quantity)], SoldAt);

    /// <summary>
    /// The general shape <see cref="CompleteAsync(SaleFixture, long, decimal)"/> is built from -
    /// any number of lines, at any timestamp - so the multi-variant and same-day-two-shifts
    /// rollup tests can compose a sale exactly the way they need to.
    /// </summary>
    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture, IReadOnlyList<SaleLineRequest> lines, DateTimeOffset soldAt)
    {
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id, shiftId, soldAt, lines, [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    /// <summary>
    /// Adds a second product to the seeded catalogue, taxed at <see cref="TaxPercent"/> and
    /// priced at <see cref="UnitPrice"/>, with an opening count posted through the ledger - the
    /// same technique <c>XReportServiceTests.SeedTaxedVariantAsync</c> uses, with round numbers so
    /// the hand-worked figures above have no rounding step to trip over.
    /// </summary>
    /// <param name="fixture">The fixture to seed into.</param>
    /// <param name="codeSuffix">
    /// Distinguishes the product code and SKU when more than one taxed variant is seeded into the
    /// same fixture (the multi-variant rollup test needs two, each attributed correctly).
    /// </param>
    /// <param name="unitPrice">The variant's selling price. Defaults to <see cref="UnitPrice"/>.</param>
    /// <param name="unitCost">
    /// The variant's average cost, both on the product row and on the opening stock posting.
    /// Defaults to 60.00, matching every existing hand-worked figure in this file.
    /// </param>
    private static Task<long> SeedTaxedVariantAsync(
        SaleFixture fixture, string codeSuffix = "001", decimal? unitPrice = null, decimal? unitCost = null)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();
        var price = unitPrice ?? UnitPrice;
        var cost = unitCost ?? 60.00m;

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var taxClass = new TaxClass
            {
                Name = "Ten percent (P3-T03) " + codeSuffix,
                Rate = TaxRate.FromPercent(TaxPercent),
                Active = true,
            };

            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = "ZREPORT-" + codeSuffix,
                Name = "Taxed widget " + codeSuffix,
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClass.Id,
                CostAvg = Money.FromDecimal(cost),
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = "A1",
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
                Sku = "ZREPORT-" + codeSuffix + "-A",
                Attributes = """{"size":"std"}""",
                Price = Money.FromDecimal(price),
                Active = true,
                CreatedAt = SoldAt,
            };

            context.Add(variant);
            await context.SaveChangesAsync(token);

            await ledger.PostAsync(
                new StockPosting(
                    variant.Id,
                    "OPENING",
                    Quantity.FromDecimal(100m, uomId),
                    Money.FromDecimal(cost),
                    "OPENING",
                    RefDocId: null,
                    userId,
                    SoldAt),
                token);

            return variant.Id;
        });
    }
}
