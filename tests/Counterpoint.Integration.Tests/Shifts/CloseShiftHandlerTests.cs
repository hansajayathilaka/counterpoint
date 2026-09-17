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

    private static async Task<CompletedSale> CompleteAsync(SaleFixture fixture, long variantId, decimal quantity)
    {
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id, shiftId, SoldAt, lines, [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    /// <summary>
    /// Adds a second product to the seeded catalogue, taxed at <see cref="TaxPercent"/> and
    /// priced at <see cref="UnitPrice"/>, with an opening count posted through the ledger - the
    /// same technique <c>XReportServiceTests.SeedTaxedVariantAsync</c> uses, with round numbers so
    /// the hand-worked figures above have no rounding step to trip over.
    /// </summary>
    private static Task<long> SeedTaxedVariantAsync(SaleFixture fixture)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var taxClass = new TaxClass
            {
                Name = "Ten percent (P3-T03)",
                Rate = TaxRate.FromPercent(TaxPercent),
                Active = true,
            };

            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = "ZREPORT-001",
                Name = "Taxed widget",
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClass.Id,
                CostAvg = Money.FromDecimal(60.00m),
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
                Sku = "ZREPORT-001-A",
                Attributes = """{"size":"std"}""",
                Price = Money.FromDecimal(UnitPrice),
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
                    Money.FromDecimal(60.00m),
                    "OPENING",
                    RefDocId: null,
                    userId,
                    SoldAt),
                token);

            return variant.Id;
        });
    }
}
