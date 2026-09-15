using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Shifts;

/// <summary>
/// The X report (task P3-T02, SRS FR-8.3, RPT-04): a mid-shift snapshot that changes nothing.
/// </summary>
public sealed class XReportServiceTests
{
    private const decimal TaxPercent = 10m;
    private const decimal UnitPrice = 100.00m;

    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";
    private const string CashierUsername = "priya";
    private const string CashierPassword = "counter1";

    private static readonly DateTimeOffset SoldAt = new(2026, 9, 10, 9, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset ReturnedAt = new(2026, 9, 10, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset MovementAt = new(2026, 9, 10, 8, 30, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_8_3_XReportFiguresMatchHandComputedValuesOnASeededShift()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await SeedReturnNumberSequenceAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // A float top-up, so the cash-in side of the drawer is exercised too.
        await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, user.Id, Money.FromDecimal(1000m), "Float top-up", MovementAt));

        // Sale A: 2 pieces @ 100.00, 10% tax -> subtotal 200.00, tax 20.00, total 220.00, paid CASH.
        var saleA = await CompleteAsync(fixture, variantId, quantity: 2m, TenderTypes.Cash);
        saleA.Total.Should().Be(Money.FromDecimal(220.00m), "the hand-worked example depends on this exact figure");

        // Sale B: 1 piece @ 100.00, 10% tax -> subtotal 100.00, tax 10.00, total 110.00, paid CARD.
        var saleB = await CompleteAsync(fixture, variantId, quantity: 1m, TenderTypes.Card);
        saleB.Total.Should().Be(Money.FromDecimal(110.00m));

        // Return 1 of the 2 pieces from Sale A, refunded CASH: half of a 200.00/20.00 line is
        // 100.00 merchandise + 10.00 tax = 110.00.
        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + saleA.SaleId + ";");

        var returned = await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            saleA.SaleId,
            user.Id,
            shiftId,
            ReturnedAt,
            [new ReturnLineRequest(
                saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable,
                "Customer changed mind")],
            RefundMethod.Cash));

        returned.TotalRefund.Should().Be(Money.FromDecimal(110.00m));

        // The clock does not move on its own (FixedTimeProvider) - advance it so
        // ShiftDuration below is a real, non-zero figure, the same technique ReprintReceiptTests
        // and BackupDashboardWarningTests already use.
        ((FixedTimeProvider)fixture.Resolve<TimeProvider>()).Advance(TimeSpan.FromHours(3));

        var report = await fixture.Resolve<IXReportService>().GenerateAsync(shiftId);

        report.ShiftId.Should().Be(shiftId);
        report.UserId.Should().Be(user.Id);
        report.CashierDisplayName.Should().NotBeNullOrWhiteSpace();

        report.SalesCount.Should().Be(2);
        report.SalesValue.Should().Be(Money.FromDecimal(330.00m), "220.00 + 110.00");
        report.DiscountTotal.Should().Be(Money.Zero);
        report.SalesTaxTotal.Should().Be(Money.FromDecimal(30.00m), "20.00 + 10.00");

        report.ReturnsCount.Should().Be(1);
        report.ReturnsValue.Should().Be(Money.FromDecimal(110.00m));
        report.ReturnsTaxTotal.Should().Be(Money.FromDecimal(10.00m));

        report.TaxBreakdown.Should().ContainSingle();
        var bracket = report.TaxBreakdown[0];
        bracket.Rate.Should().Be(TaxRate.FromPercent(TaxPercent));
        bracket.TaxableAmount.Should().Be(Money.FromDecimal(270.00m), "180.00 + 90.00 net of tax");
        bracket.TaxAmount.Should().Be(Money.FromDecimal(30.00m));

        report.Tenders.Should().HaveCount(2);
        var cash = report.Tenders.Single(t => t.TenderType == TenderTypes.Cash);
        cash.SalesAmount.Should().Be(Money.FromDecimal(220.00m));
        cash.RefundsAmount.Should().Be(Money.FromDecimal(110.00m));
        cash.NetAmount.Should().Be(Money.FromDecimal(110.00m));

        var card = report.Tenders.Single(t => t.TenderType == TenderTypes.Card);
        card.SalesAmount.Should().Be(Money.FromDecimal(110.00m));
        card.RefundsAmount.Should().Be(Money.Zero);
        card.NetAmount.Should().Be(Money.FromDecimal(110.00m));

        report.CashMovements.Should().ContainSingle(m => m.Reason == "Float top-up");

        // The single expected-cash formula (P3-T01), never reimplemented here: 0 + 220.00 -
        // 110.00 + 1000.00 - 0 = 1110.00.
        report.ExpectedCash.OpeningFloat.Should().Be(Money.Zero);
        report.ExpectedCash.CashSales.Should().Be(Money.FromDecimal(220.00m));
        report.ExpectedCash.CashRefunds.Should().Be(Money.FromDecimal(110.00m));
        report.ExpectedCash.CashIn.Should().Be(Money.FromDecimal(1000m));
        report.ExpectedCash.CashOut.Should().Be(Money.Zero);
        report.ExpectedCash.ExpectedCash.Should().Be(Money.FromDecimal(1110.00m));

        report.ShiftDuration.Should().Be(TimeSpan.FromHours(3), "the clock advanced exactly 3 hours since the shift opened");
    }

    /// <summary>
    /// The other hand-computed test above deliberately has no discount at all
    /// (<c>DiscountTotal.Should().Be(Money.Zero)</c>), which proves nothing about
    /// <c>XReportFiguresReader</c>'s <c>SUM(line_discount + bill_discount)</c> - a reader with the
    /// wrong column, a missing addend or a sign error would still pass a shift with no discount on
    /// it. This seeds a bill carrying both a line discount and a bill discount together, so
    /// <see cref="XReportSummary.DiscountTotal"/> is only right if both addends, and the summation
    /// itself, are right (task P3-T02 "Do this" #1: "discounts").
    /// </summary>
    [Fact]
    public async Task FR_8_3_XReportDiscountTotalSumsTheLineDiscountAndTheBillDiscountTogether()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // 3 pieces @ 100.00 = 300.00 gross, less a 10% line discount (30.00) -> line total 270.00,
        // 10% tax on that net figure = 27.00. Less a further 5.00 flat bill discount -> total
        // 270.00 - 5.00 + 27.00 = 292.00. Every figure below is chosen to land on a whole cent, so
        // there is no rounding step to obscure whether the sum is right.
        var lines = new List<SaleLineRequest>
        {
            new(variantId, 3m, Discount: DiscountInput.OfRate(Percentage.FromPercent(10m))),
        };

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(
            lines, billDiscount: DiscountInput.OfAmount(Money.FromDecimal(5.00m)));
        quote.Total.Should().Be(Money.FromDecimal(292.00m), "the hand-worked example depends on this exact figure");

        var completed = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id,
            shiftId,
            SoldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)],
            BillDiscount: DiscountInput.OfAmount(Money.FromDecimal(5.00m))));

        completed.Total.Should().Be(Money.FromDecimal(292.00m));

        var lineDiscountStored = await fixture.ScalarAsync(
            "SELECT line_discount FROM sale WHERE id = " + completed.SaleId + ";");
        lineDiscountStored.Should().Be(Money.FromDecimal(30.00m).ToScaled().ToString(CultureInfo.InvariantCulture));

        var billDiscountStored = await fixture.ScalarAsync(
            "SELECT bill_discount FROM sale WHERE id = " + completed.SaleId + ";");
        billDiscountStored.Should().Be(Money.FromDecimal(5.00m).ToScaled().ToString(CultureInfo.InvariantCulture));

        var report = await fixture.Resolve<IXReportService>().GenerateAsync(shiftId);

        report.SalesCount.Should().Be(1);
        report.SalesValue.Should().Be(Money.FromDecimal(292.00m));
        report.DiscountTotal.Should().Be(
            Money.FromDecimal(35.00m), "30.00 line discount + 5.00 bill discount - both addends, not just one");
        report.SalesTaxTotal.Should().Be(Money.FromDecimal(27.00m));

        report.TaxBreakdown.Should().ContainSingle();
        report.TaxBreakdown[0].TaxableAmount.Should().Be(Money.FromDecimal(243.00m), "270.00 - 27.00 tax");
        report.TaxBreakdown[0].TaxAmount.Should().Be(Money.FromDecimal(27.00m));
    }

    [Fact]
    public async Task P3_T02_TakingTenXReportsChangesNoDataWhatsoever()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        // A shift with real activity on it, not an empty one - counting sale_line and payment
        // rows before and after only means something once there are some to disturb, and this is
        // the same activity (a cash-in, two sales, a return) the hand-computed test above already
        // builds, so a regression here is a regression against a known-good shape.
        await SeedShiftActivityAsync(fixture);

        var before = await SnapshotAsync(fixture, shiftId);

        for (var i = 0; i < 10; i++)
        {
            await fixture.Resolve<IXReportService>().GenerateAsync(shiftId);
        }

        var after = await SnapshotAsync(fixture, shiftId);

        after.ShiftRow.Should().Be(before.ShiftRow, "task P3-T02's own risk note: an X report must be free of side effects");
        after.AuditLogCount.Should().Be(before.AuditLogCount);
        after.PrintJobCount.Should().Be(before.PrintJobCount);
        after.CashMovementCount.Should().Be(before.CashMovementCount);
        after.SaleCount.Should().Be(before.SaleCount);
        after.SaleReturnCount.Should().Be(before.SaleReturnCount);

        // The two tables IXReportFiguresReader actually reads beyond sale and sale_return itself
        // - sale_line for the tax breakdown, payment for the tender breakdown - were not checked
        // at all before; a reader with an accidental write in it could corrupt either without
        // tripping any of the counts above.
        after.SaleLineCount.Should().Be(before.SaleLineCount, "sale_line is read for the tax breakdown");
        after.PaymentCount.Should().Be(before.PaymentCount, "payment is read for the tender breakdown");

        before.SaleLineCount.Should().BeGreaterThan(0, "the seeded activity must leave something for this assertion to actually cover");
        before.PaymentCount.Should().BeGreaterThan(0, "the seeded activity must leave something for this assertion to actually cover");
    }

    [Fact]
    public async Task P3_T02_PrintingTenXReportsAlsoChangesNoDataWhatsoever()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        await SeedShiftActivityAsync(fixture);

        var before = await SnapshotAsync(fixture, shiftId);
        var clock = (FixedTimeProvider)fixture.Resolve<TimeProvider>();

        for (var i = 0; i < 10; i++)
        {
            var outcome = await fixture.Resolve<IXReportPrintService>().PrintAsync(shiftId);
            outcome.Succeeded.Should().BeTrue();

            // FileReceiptPrinter names each file from the clock, to millisecond precision; move
            // it on so ten prints leave ten files rather than nine overwrites of the same one -
            // a test-fixture artefact of the frozen clock, not something this service does.
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var after = await SnapshotAsync(fixture, shiftId);

        after.ShiftRow.Should().Be(before.ShiftRow);
        after.AuditLogCount.Should().Be(before.AuditLogCount);
        after.PrintJobCount.Should().Be(
            before.PrintJobCount, "an X report prints directly - it is not a print_job outbox row (CLAUDE.md invariant 7's outbox rule does not apply to a document with no transaction)");
        after.CashMovementCount.Should().Be(before.CashMovementCount);
        after.SaleCount.Should().Be(before.SaleCount);
        after.SaleReturnCount.Should().Be(before.SaleReturnCount);
        after.SaleLineCount.Should().Be(before.SaleLineCount, "sale_line is read for the tax breakdown");
        after.PaymentCount.Should().Be(before.PaymentCount, "payment is read for the tender breakdown");

        Directory.GetFiles(fixture.ReceiptDirectory, "*.bin").Should().HaveCount(10);
    }

    [Fact]
    public async Task FR_8_3_ItPrintsCorrectlyOnTheThermalPrinter()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var outcome = await fixture.Resolve<IXReportPrintService>().PrintAsync(shiftId);

        outcome.Succeeded.Should().BeTrue();
        outcome.Target.Should().NotBeNullOrWhiteSpace();

        var files = Directory.GetFiles(fixture.ReceiptDirectory, "*.bin");
        files.Should().ContainSingle();

        var bytes = await File.ReadAllBytesAsync(files.Single());
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task P3_T02_ACashierCanTakeAnXReportForTheirOwnShiftButNotForAnotherShift()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var firstShiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var ownReport = await fixture.Resolve<IXReportService>().GenerateAsync(firstShiftId);
        ownReport.ShiftId.Should().Be(firstShiftId);

        // Close the shift directly (P3-T03's own close handler does not exist yet, the same
        // technique CashMovementServiceTests uses) and open a second one, so there is a shift
        // the signed-in cashier is not currently trading in.
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(cashier.Id, Money.Zero, MovementAt.AddHours(1)));

        var attempt = () => fixture.Resolve<IXReportService>().GenerateAsync(firstShiftId);
        await attempt.Should().ThrowAsync<NotAuthorisedException>(
            "a cashier may only take an X report for the shift they are currently trading in");

        // An owner may take one for any shift.
        await AsOwnerAsync(fixture, async () =>
        {
            var ownerReport = await fixture.Resolve<IXReportService>().GenerateAsync(firstShiftId);
            ownerReport.ShiftId.Should().Be(firstShiftId);
        });
    }

    [Fact]
    public async Task NobodySignedInCannotTakeAnXReport()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var attempt = () => fixture.Resolve<IXReportService>().GenerateAsync(1);
        await attempt.Should().ThrowAsync<NotAuthorisedException>();
    }

    private static async Task AsOwnerAsync(SaleFixture fixture, Func<Task> action)
    {
        var authentication = fixture.Resolve<IAuthenticationService>();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(Owner, OwnerPassword)).Succeeded.Should().BeTrue();

        await action();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(CashierUsername, CashierPassword)).Succeeded.Should().BeTrue();
    }

    /// <summary>An owner account with a password, a cashier account, and the cashier signed in - same shape as CashMovementServiceTests.</summary>
    private static async Task<SaleFixture> SignedInAsCashierAsync()
    {
        var fixture = await SaleFixture.CreateAsync();

        try
        {
            await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);

            var authentication = fixture.Resolve<IAuthenticationService>();
            await authentication.LogInAsync(Owner, OwnerPassword);

            await fixture.Resolve<IUserAdministration>().CreateAsync(
                new CreateUserCommand(CashierUsername, "Priya", CashierPassword, Role.Cashier));

            await authentication.LogOutAsync();
            (await authentication.LogInAsync(CashierUsername, CashierPassword)).Succeeded.Should().BeTrue();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static async Task<(
        string ShiftRow,
        long AuditLogCount,
        long PrintJobCount,
        long CashMovementCount,
        long SaleCount,
        long SaleReturnCount,
        long SaleLineCount,
        long PaymentCount)> SnapshotAsync(
        SaleFixture fixture, long shiftId)
    {
        var shiftRow = await fixture.ScalarAsync(
            "SELECT id || '|' || shift_no || '|' || user_id || '|' || opened_at || '|' || business_date "
            + "|| '|' || opening_float || '|' || COALESCE(closed_at,'') || '|' || COALESCE(counted_cash,'') "
            + "|| '|' || COALESCE(expected_cash,'') || '|' || COALESCE(variance,'') || '|' || status "
            + "|| '|' || COALESCE(closed_by,'') || '|' || COALESCE(note,'') "
            + "FROM shift WHERE id = " + shiftId.ToString(CultureInfo.InvariantCulture) + ";");

        var auditLogCount = await fixture.CountAsync("SELECT COUNT(*) FROM audit_log;");
        var printJobCount = await fixture.CountAsync("SELECT COUNT(*) FROM print_job;");
        var cashMovementCount = await fixture.CountAsync("SELECT COUNT(*) FROM cash_movement;");
        var saleCount = await fixture.CountAsync("SELECT COUNT(*) FROM sale;");
        var saleReturnCount = await fixture.CountAsync("SELECT COUNT(*) FROM sale_return;");

        // The two tables IXReportFiguresReader queries directly beyond sale/sale_return
        // themselves (task P3-T02 audit: "does it actually cover every table an X report
        // touches?") - sale_line for the tax breakdown, payment for the tender breakdown.
        var saleLineCount = await fixture.CountAsync("SELECT COUNT(*) FROM sale_line;");
        var paymentCount = await fixture.CountAsync("SELECT COUNT(*) FROM payment;");

        return (
            shiftRow ?? string.Empty,
            auditLogCount,
            printJobCount,
            cashMovementCount,
            saleCount,
            saleReturnCount,
            saleLineCount,
            paymentCount);
    }

    /// <summary>
    /// A float top-up, two completed sales and a return against one of them - real activity for
    /// the "changes no data whatsoever" tests to disturb, rather than an empty shift where every
    /// row count is trivially zero both before and after. The same shape
    /// <c>FR_8_3_XReportFiguresMatchHandComputedValuesOnASeededShift</c> builds.
    /// </summary>
    private static async Task SeedShiftActivityAsync(SaleFixture fixture)
    {
        await SeedReturnNumberSequenceAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, user.Id, Money.FromDecimal(1000m), "Float top-up", MovementAt));

        var saleA = await CompleteAsync(fixture, variantId, quantity: 2m, TenderTypes.Cash);
        await CompleteAsync(fixture, variantId, quantity: 1m, TenderTypes.Card);

        var saleLineId = await fixture.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + saleA.SaleId + ";");

        await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            saleA.SaleId,
            user.Id,
            shiftId,
            ReturnedAt,
            [new ReturnLineRequest(
                saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable,
                "Customer changed mind")],
            RefundMethod.Cash));
    }

    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture, long variantId, decimal quantity, string tenderType)
    {
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id, shiftId, SoldAt, lines, [new TenderRequest(tenderType, quote.Total)]));
    }

    /// <summary>
    /// Adds a second product to the seeded catalogue, taxed at <see cref="TaxPercent"/> and
    /// priced at <see cref="UnitPrice"/>, with an opening count posted through the ledger - the
    /// same technique <c>TaxedSaleTests.SeedTaxedVariantAsync</c> uses, with round numbers so the
    /// hand-worked figures above have no rounding step to trip over.
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
                Name = "Ten percent",
                Rate = TaxRate.FromPercent(TaxPercent),
                Active = true,
            };

            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = "XREPORT-001",
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
                Sku = "XREPORT-001-A",
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
