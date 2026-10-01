using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// <b>AC-12 for the P3-T05 reports, generatively.</b> A seeded random trading history - bills with
/// line and bill discounts (fractional allocations included), open items, a multi-unit product,
/// several tax rates, cancellations, linked and unlinked returns (SELLABLE and DAMAGED), cost
/// changes mid-history, and a shift close every day - is checked, after every day both while its
/// shift is open and after it is closed and rolled up, on the whole range, on the day and on a
/// random sub-range.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is asserted.</b> Every headline figure equals an independent recomputation written in
/// this file straight from <c>sale</c>, <c>sale_return</c>, <c>sale_return_line</c> and
/// <c>payment</c> (scaled-integer SQL sums, never the report layer). The day rows, the hour rows, the
/// tender rows, the item rows, the category rows, the brand rows, the month rows, the bill list and
/// the returns report's groupings each add up to that same headline. A range that spans rolled-up
/// days and the open shift equals the same range read from raw tables only.
/// </para>
/// <para>
/// <b>A failure here is never a rounding artefact.</b> Every column is an exact scaled integer and
/// every slice uses the same canonical formula, so the slices must equal the headline to the last
/// 0.0001. One place a difference is conceivable - COGS by item/category/brand, which multiplies each
/// line's own cost where the per-day and headline figures use the per-sale <c>sale.cogs</c> header -
/// is asserted exactly too; if the two ever part ways the test fails rather than hiding it behind a
/// tolerance, so the documented sub-0.0001 allowance is a finding to read, not a margin to spend.
/// </para>
/// </remarks>
public sealed class SalesReportReconciliationTests
{
    private const int Days = 30;
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";

    private static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);
    private static readonly DateOnly FirstDay = new(2026, 9, 6);

    private static readonly string[] Reasons = ["Changed mind", "Wrong size", "Faulty", "Duplicate purchase"];

    [Theory]
    [InlineData(false, 20260930)]
    [InlineData(true, 20261001)]
    public async Task AC_12_EverySliceOfEveryP3T05ReportAddsUpToTheHeadlineOnARandomTradingHistory(bool pricesIncludeTax, int seed)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.SeedReturnNumberSequenceAsync(fixture);
        await SalesReportDataset.DisableBackupOnShiftCloseAsync(fixture);
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax);
        await fixture.Resolve<ISettings>().UpdateAsync(
            settings => settings with { Policy = settings.Policy with { AllowUnlinkedReturns = true } });

        var session = fixture.Resolve<ISession>();
        var owner = session.CurrentUser!;
        var pool = await SeedPoolAsync(fixture);
        var random = new Random(seed);

        var bills = new List<SimBill>();
        var checks = 0;

        for (var dayIndex = 0; dayIndex < Days; dayIndex++)
        {
            var date = FirstDay.AddDays(dayIndex);

            if (dayIndex > 0)
            {
                await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(owner.Id, Money.Zero, At(date, 8, 0)));
            }

            var shiftId = session.ShiftId!.Value;
            var clock = At(date, 8, 30);

            // A quiet day now and then: a shift with nothing in it still rolls up (as zeros) and
            // the reports must read it without a row of its own.
            var operations = random.Next(12) == 0 ? 0 : random.Next(6, 15);

            for (var i = 0; i < operations; i++)
            {
                clock = clock.AddMinutes(random.Next(3, 50));
                if (clock.Hour > 20)
                {
                    break;
                }

                var roll = random.Next(100);
                var today = bills.Where(bill => bill.Date == date && !bill.Cancelled && !bill.HasReturns).ToList();
                var returnable = bills.Where(bill => !bill.Cancelled && date.DayNumber - bill.Date.DayNumber <= 10
                    && bill.Lines.Any(line => line.VariantId is not null && line.RemainingBase > 0)).ToList();

                if (roll < 60 || bills.Count == 0)
                {
                    bills.Add(await SellAsync(fixture, pool, random, owner.Id, shiftId, clock, date));
                }
                else if (roll < 68 && today.Count > 0)
                {
                    var victim = today[random.Next(today.Count)];
                    await fixture.Resolve<ICancelSale>().CancelAsync(
                        new CancelSaleCommand(victim.SaleId, "Rung up by mistake", clock));
                    victim.Cancelled = true;
                }
                else if (roll < 88 && returnable.Count > 0)
                {
                    await LinkedReturnAsync(fixture, random, returnable[random.Next(returnable.Count)], owner.Id, shiftId, clock);
                }
                else if (roll < 94)
                {
                    await UnlinkedReturnAsync(fixture, pool, random, owner.Id, shiftId, clock);
                }
                else
                {
                    await ChangeCostAsync(fixture, pool, random, owner.Id, clock);
                }
            }

            // Open shift: today is read from raw tables, every earlier day from its rollup.
            checks += await VerifyRangesAsync(fixture, pool, random, date, $"day {dayIndex} open");

            if (dayIndex < Days - 1)
            {
                await fixture.Resolve<ICloseShift>().CloseAsync(
                    new CloseShiftCommand(shiftId, owner.Id, Money.Zero, At(date, 21, 0), Note: "reconciliation run"));

                // Closed: every day so far is rolled up (subject to a cancellation having overtaken it).
                checks += await VerifyRangesAsync(fixture, pool, random, date, $"day {dayIndex} closed");
            }
        }

        checks.Should().BeGreaterThan(150, "the run must actually have verified hundreds of range/state combinations");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'COMPLETED';"))
            .Should().BeGreaterThan(80, "the random history must contain real trading");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'CANCELLED';"))
            .Should().BeGreaterThan(0, "the history must include cancellations");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return WHERE original_sale_id IS NULL;"))
            .Should().BeGreaterThan(0, "the history must include unlinked returns");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return_line WHERE disposition = 'DAMAGED';"))
            .Should().BeGreaterThan(0, "the history must include damaged returns");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_line WHERE product_variant_id IS NULL;"))
            .Should().BeGreaterThan(0, "the history must include open items");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE line_discount > 0;"))
            .Should().BeGreaterThan(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE bill_discount > 0;"))
            .Should().BeGreaterThan(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM daily_sales_summary;"))
            .Should().Be(Days - 1, "every closed day has a rollup, so the routed path really read rollups");
    }

    // ---- The checks ----------------------------------------------------------------------------

    private static async Task<int> VerifyRangesAsync(
        SaleFixture fixture, Pool pool, Random random, DateOnly today, string label)
    {
        var span = today.DayNumber - FirstDay.DayNumber;
        var from = FirstDay.AddDays(random.Next(span + 1));
        var to = from.AddDays(random.Next(today.DayNumber - from.DayNumber + 1));

        await VerifyAsync(fixture, pool, ReportDateRange.Custom(FirstDay, today), label + ", whole history");
        await VerifyAsync(fixture, pool, ReportDateRange.Custom(today, today), label + ", today only");
        await VerifyAsync(fixture, pool, ReportDateRange.Custom(from, to), label + $", {from:yyyy-MM-dd}..{to:yyyy-MM-dd}");

        return 3;
    }

    private static async Task VerifyAsync(SaleFixture fixture, Pool pool, ReportDateRange range, string label)
    {
        var raw = await ReadRawAsync(fixture, range);
        var because = "[" + label + " " + range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + ".." + range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "]";

        // ---- RPT-01 ----
        var summary = await fixture.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(range);
        var totals = summary.Totals;

        totals.BillCount.Should().Be(raw.Bills, because);
        totals.ReturnCount.Should().Be(raw.Returns, because);
        totals.GrossSales.Should().Be(Money.FromScaled(raw.Subtotal + raw.LineDiscount), because);
        totals.Discounts.Should().Be(Money.FromScaled(raw.LineDiscount + raw.BillDiscount), because);
        totals.Tax.Should().Be(Money.FromScaled(raw.Tax), because);
        totals.NetSales.Should().Be(Money.FromScaled(raw.Subtotal - raw.BillDiscount - raw.ReturnSubtotal), because);
        totals.ReturnsValue.Should().Be(Money.FromScaled(raw.ReturnRefund), because);
        totals.TenderTotal.Should().Be(Money.FromScaled(raw.Tender), because);

        // The routed headline equals raw-only, which is what makes the rollup boundary safe.
        var period = fixture.Resolve<ISalesPeriodSummaryQuery>();
        (await period.GetSalesSummaryAsync(range)).Should().Be(totals, because);
        (await period.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired)).Should().Be(totals, because);

        summary.ByDay.Sum(row => row.BillCount).Should().Be(totals.BillCount, because);
        summary.ByDay.Sum(row => row.ReturnCount).Should().Be(totals.ReturnCount, because);
        Sum(summary.ByDay.Select(row => row.Gross)).Should().Be(totals.GrossSales, because + " day gross");
        Sum(summary.ByDay.Select(row => row.Discounts)).Should().Be(totals.Discounts, because + " day discounts");
        Sum(summary.ByDay.Select(row => row.Tax)).Should().Be(totals.Tax, because + " day tax");
        Sum(summary.ByDay.Select(row => row.Net)).Should().Be(totals.NetSales, because + " day net");
        Sum(summary.ByDay.Select(row => row.ReturnsValue)).Should().Be(totals.ReturnsValue, because + " day returns");
        summary.ByDay.Select(row => row.BusinessDate).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        summary.ByDay.All(row => row.BusinessDate >= range.From && row.BusinessDate <= range.To)
            .Should().BeTrue(because + " every day row lies inside the range");

        summary.ByHour.Sum(row => row.BillCount).Should().Be(totals.BillCount, because + " hour bills");
        Sum(summary.ByHour.Select(row => row.Gross)).Should().Be(totals.GrossSales, because + " hour gross");
        Sum(summary.ByHour.Select(row => row.Discounts)).Should().Be(totals.Discounts, because + " hour discounts");
        Sum(summary.ByHour.Select(row => row.Tax)).Should().Be(totals.Tax, because + " hour tax");
        Sum(summary.ByHour.Select(row => row.Net)).Should().Be(totals.NetSales, because + " hour net");
        summary.ByHour.Select(row => row.Hour).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();

        Sum(summary.ByTender.Select(row => row.NetAmount)).Should().Be(totals.TenderTotal, because + " tender net");
        Sum(summary.ByTender.Select(row => row.RefundsAmount)).Should().Be(
            Money.FromScaled(raw.RefundTender), because + " tender refunds");

        if (totals.BillCount > 0)
        {
            summary.AverageBillValue.Amount.Should().BeApproximately(
                (totals.GrossSales - totals.Discounts).Amount / totals.BillCount, 0.0001m, because + " average bill");
        }
        else
        {
            summary.AverageBillValue.Should().Be(Money.Zero, because);
        }

        // ---- RPT-02 ----
        var breakdown = fixture.Resolve<ISalesBreakdownQuery>();
        foreach (var dimension in Enum.GetValues<SalesBreakdownDimension>())
        {
            var report = await breakdown.GetBreakdownAsync(range, dimension);

            report.TotalNet.Should().Be(totals.NetSales, because + " " + dimension + " total");
            Sum(report.Rows.Select(row => row.Net)).Should().Be(totals.NetSales, because + " " + dimension + " rows");
            report.Rows.Select(row => row.Net).Should().BeInDescendingOrder(because);
            report.Rows.Select(row => row.Rank).Should().Equal(Enumerable.Range(1, report.Rows.Count), because);

            if (!totals.NetSales.IsZero)
            {
                report.Rows.Sum(row => row.ShareOfNet).Should().BeApproximately(1m, 0.0000001m, because);
            }
        }

        // ---- RPT-03 ----
        var profit = fixture.Resolve<IProfitReportQuery>();
        var expectedCogs = Money.FromScaled(raw.Cogs);
        foreach (var grouping in Enum.GetValues<ProfitGrouping>())
        {
            var report = await profit.GetProfitReportAsync(range, grouping);

            report.Totals.NetSales.Should().Be(totals.NetSales, because + " " + grouping);
            report.Totals.Cogs.Should().Be(expectedCogs, because + " " + grouping + " headline cogs");
            report.Totals.GrossProfit.Should().Be(totals.NetSales - expectedCogs, because);
            Sum(report.Rows.Select(row => row.Net)).Should().Be(totals.NetSales, because + " " + grouping + " net");
            Sum(report.Rows.Select(row => row.Cogs)).Should().Be(expectedCogs, because + " " + grouping + " cogs");
            Sum(report.Rows.Select(row => row.GrossProfit)).Should().Be(report.Totals.GrossProfit, because + " " + grouping + " profit");

            if (grouping is ProfitGrouping.Day or ProfitGrouping.Month)
            {
                report.Rows.All(row => row.Period is not null).Should().BeTrue(because);
                report.Rows.SelectMany(row => new[] { row.Period!.From, row.Period.To })
                    .All(day => day >= range.From && day <= range.To)
                    .Should().BeTrue(because + " drill ranges stay inside the report range");
            }
        }

        // The profit headline routed equals raw-only, and is independent of which path read it.
        var profitPeriod = fixture.Resolve<IProfitPeriodSummaryQuery>();
        (await profitPeriod.GetProfitSummaryAsync(range)).Should().Be(
            await profitPeriod.GetProfitSummaryAsync(range, ReportSourcePolicy.RawTablesRequired), because);

        // ---- Returns report ----
        var returns = await fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(range);
        returns.ReturnCount.Should().Be(raw.Returns, because);
        returns.ReturnsSubtotal.Should().Be(Money.FromScaled(raw.ReturnSubtotal), because);
        returns.TotalRefunded.Should().Be(Money.FromScaled(raw.ReturnRefund), because);
        returns.SalesBeforeReturns.Should().Be(totals.NetSales + returns.ReturnsSubtotal, because);
        returns.BillCount.Should().Be(raw.Bills, because);
        foreach (var (name, rows) in new (string, IReadOnlyList<ReturnsGroupRow>)[]
        {
            ("reason", returns.ByReason), ("item", returns.ByItem), ("disposition", returns.ByDisposition),
            ("linkage", returns.ByLinkage), ("method", returns.ByRefundMethod),
        })
        {
            Sum(rows.Select(row => row.Value)).Should().Be(returns.ReturnsSubtotal, because + " returns by " + name);
        }

        returns.ByLinkage.Sum(row => row.Count).Should().Be(raw.Returns, because);
        returns.ByRefundMethod.Sum(row => row.Count).Should().Be(raw.Returns, because);
        returns.ByDisposition.Sum(row => row.Count).Should().Be(raw.ReturnLines, because);
        returns.ByReason.Sum(row => row.Count).Should().Be(raw.ReturnLines, because);
        returns.ByItem.Sum(row => row.Count).Should().Be(raw.ReturnLines, because);

        // ---- Drill-down ----
        var billQuery = fixture.Resolve<ISalesBillQuery>();
        var list = await billQuery.GetBillsAsync(new BillListFilter(range));
        list.IsTruncated.Should().BeFalse(because);
        list.Rows.Should().HaveCount(raw.Bills, because);
        Sum(list.Rows.Select(row => row.Gross - row.Discounts)).Should().Be(totals.GrossSales - totals.Discounts, because + " bill net before returns");
        Sum(list.Rows.Select(row => row.Net)).Should().Be(totals.GrossSales - totals.Discounts, because);
        Sum(list.Rows.Select(row => row.Tax)).Should().Be(totals.Tax, because);
        Sum(list.Rows.Select(row => row.Total)).Should().Be(Money.FromScaled(raw.BillTotals), because);
        list.Rows.Select(row => row.SoldAt).Should().BeInAscendingOrder(because);

        foreach (var hourRow in summary.ByHour.Where(row => row.BillCount > 0))
        {
            var hourList = await billQuery.GetBillsAsync(new BillListFilter(range, Hour: hourRow.Hour));
            hourList.Rows.Should().HaveCount(hourRow.BillCount, because + " hour " + hourRow.Hour);
            Sum(hourList.Rows.Select(row => row.Net)).Should().Be(hourRow.Gross - hourRow.Discounts, because + " hour " + hourRow.Hour);
        }

        foreach (var dayRow in summary.ByDay.Where(row => row.BillCount > 0))
        {
            var dayList = await billQuery.GetBillsAsync(
                new BillListFilter(ReportDateRange.Custom(dayRow.BusinessDate, dayRow.BusinessDate)));
            dayList.Rows.Should().HaveCount(dayRow.BillCount, because + " day " + dayRow.BusinessDate);
            Sum(dayList.Rows.Select(row => row.Net)).Should().Be(dayRow.Gross - dayRow.Discounts, because);
        }

        foreach (var variantId in pool.VariantIds)
        {
            var expected = await fixture.CountAsync(
                "SELECT COUNT(DISTINCT s.id) FROM sale s JOIN sale_line sl ON sl.sale_id = s.id "
                + "WHERE s.status = 'COMPLETED' AND s.business_date >= '" + Text(range.From)
                + "' AND s.business_date <= '" + Text(range.To) + "' AND sl.product_variant_id = "
                + variantId.ToString(CultureInfo.InvariantCulture) + ";");
            var byVariant = await billQuery.GetBillsAsync(new BillListFilter(range, ProductVariantId: variantId));
            byVariant.Rows.Should().HaveCount((int)expected, because + " variant " + variantId);
        }
    }

    private sealed record Raw(
        int Bills, int Returns, int ReturnLines, long Subtotal, long LineDiscount, long BillDiscount, long Tax,
        long BillTotals, long ReturnSubtotal, long ReturnRefund, long Tender, long RefundTender, long Cogs);

    /// <summary>
    /// The independent recomputation: scaled-integer sums straight from the tables, written here and
    /// nowhere in the report layer. Return cost is unit_cost x qty_base over SELLABLE lines only; every
    /// quantity in this history is a whole number of base units so the division by the quantity
    /// scale is exact.
    /// </summary>
    private static async Task<Raw> ReadRawAsync(SaleFixture fixture, ReportDateRange range)
    {
        var sales = "FROM sale WHERE status = 'COMPLETED' AND business_date >= '" + Text(range.From)
            + "' AND business_date <= '" + Text(range.To) + "'";
        var returns = "FROM sale_return WHERE business_date >= '" + Text(range.From)
            + "' AND business_date <= '" + Text(range.To) + "'";
        var saleIds = "SELECT id " + sales;
        var returnIds = "SELECT id " + returns;

        var saleCogs = await fixture.CountAsync("SELECT COALESCE(SUM(cogs), 0) " + sales + ";");
        var returnCogs = await fixture.CountAsync(
            "SELECT COALESCE(SUM(unit_cost * qty_base / 10000), 0) FROM sale_return_line "
            + "WHERE disposition = 'SELLABLE' AND sale_return_id IN (" + returnIds + ");");

        var salesTender = await fixture.CountAsync(
            "SELECT COALESCE(SUM(amount), 0) FROM payment WHERE sale_id IN (" + saleIds + ");");
        var refundTender = await fixture.CountAsync(
            "SELECT COALESCE(SUM(-amount), 0) FROM payment WHERE sale_return_id IN (" + returnIds + ");");

        return new Raw(
            (int)await fixture.CountAsync("SELECT COUNT(*) " + sales + ";"),
            (int)await fixture.CountAsync("SELECT COUNT(*) " + returns + ";"),
            (int)await fixture.CountAsync("SELECT COUNT(*) FROM sale_return_line WHERE sale_return_id IN (" + returnIds + ");"),
            await fixture.CountAsync("SELECT COALESCE(SUM(subtotal), 0) " + sales + ";"),
            await fixture.CountAsync("SELECT COALESCE(SUM(line_discount), 0) " + sales + ";"),
            await fixture.CountAsync("SELECT COALESCE(SUM(bill_discount), 0) " + sales + ";"),
            await fixture.CountAsync("SELECT COALESCE(SUM(tax), 0) " + sales + ";"),
            await fixture.CountAsync("SELECT COALESCE(SUM(total), 0) " + sales + ";"),
            await fixture.CountAsync("SELECT COALESCE(SUM(subtotal), 0) " + returns + ";"),
            await fixture.CountAsync("SELECT COALESCE(SUM(total_refund), 0) " + returns + ";"),
            salesTender - refundTender,
            refundTender,
            saleCogs - returnCogs);
    }

    // ---- The random history --------------------------------------------------------------------

    private static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new(date.Year, date.Month, date.Day, hour, minute, 0, Offset);

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Money Sum(IEnumerable<Money> amounts) => amounts.Aggregate(Money.Zero, (sum, amount) => sum + amount);

    private sealed class SimLine(long id, long? variantId, decimal soldBase)
    {
        internal long Id { get; } = id;

        internal long? VariantId { get; } = variantId;

        internal decimal RemainingBase { get; set; } = soldBase;
    }

    private sealed class SimBill(long saleId, DateOnly date, List<SimLine> lines)
    {
        internal long SaleId { get; } = saleId;

        internal DateOnly Date { get; } = date;

        internal List<SimLine> Lines { get; } = lines;

        internal bool Cancelled { get; set; }

        internal bool HasReturns { get; set; }
    }

    private static async Task<SimBill> SellAsync(
        SaleFixture fixture, Pool pool, Random random, long userId, long shiftId, DateTimeOffset at, DateOnly date)
    {
        var lines = new List<SaleLineRequest>();
        var lineCount = random.Next(1, 5);

        for (var i = 0; i < lineCount; i++)
        {
            DiscountInput? discount = random.Next(5) == 0
                ? random.Next(2) == 0
                    ? DiscountInput.OfRate(Percentage.FromPercent(random.Next(1, 31) / 2m))
                    : DiscountInput.OfAmount(Money.FromDecimal(random.Next(1, 500) / 100m))
                : null;

            if (random.Next(10) == 0)
            {
                lines.Add(new SaleLineRequest(
                    null,
                    random.Next(1, 4),
                    UomId: pool.PieceUomId,
                    OpenItemDescription: "Service " + random.Next(1, 9),
                    OpenItemUnitPrice: Money.FromDecimal(random.Next(500, 8000) / 100m),
                    Discount: discount));
                continue;
            }

            var product = pool.Products[random.Next(pool.Products.Count)];
            var useBox = product.BoxUomId is not null && random.Next(3) == 0;
            lines.Add(new SaleLineRequest(
                product.VariantId,
                random.Next(1, 6),
                UomId: useBox ? product.BoxUomId : null,
                Discount: discount));
        }

        DiscountInput? billDiscount = random.Next(5) == 0
            ? random.Next(2) == 0
                ? DiscountInput.OfRate(Percentage.FromPercent(random.Next(1, 25) / 2m))
                : DiscountInput.OfAmount(Money.FromDecimal(random.Next(1, 300) / 100m))
            : null;

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines, billDiscount);

        var total = quote.Total.Amount;
        var tenders = new List<TenderRequest>();
        if (total > 0m && random.Next(3) == 0)
        {
            var cash = Math.Round(total * random.Next(1, 10) / 10m, 2);
            tenders.Add(new TenderRequest(TenderTypes.Cash, Money.FromDecimal(cash)));
            if (total - cash > 0m)
            {
                tenders.Add(new TenderRequest(TenderTypes.Card, Money.FromDecimal(total - cash)));
            }
        }
        else
        {
            tenders.Add(new TenderRequest(random.Next(2) == 0 ? TenderTypes.Cash : TenderTypes.Card, quote.Total));
        }

        var completed = await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, at, lines, tenders, BillDiscount: billDiscount));

        var rows = await ReadRowsAsync(
            fixture,
            "SELECT id, product_variant_id, qty_base FROM sale_line WHERE sale_id = "
            + completed.SaleId.ToString(CultureInfo.InvariantCulture) + " ORDER BY line_no;");

        return new SimBill(
            completed.SaleId,
            date,
            [.. rows.Select(row => new SimLine(
                Convert.ToInt64(row[0], CultureInfo.InvariantCulture),
                row[1] is null or DBNull ? null : Convert.ToInt64(row[1], CultureInfo.InvariantCulture),
                Convert.ToInt64(row[2], CultureInfo.InvariantCulture) / 10000m))]);
    }

    private static async Task LinkedReturnAsync(
        SaleFixture fixture, Random random, SimBill bill, long userId, long shiftId, DateTimeOffset at)
    {
        var candidates = bill.Lines.Where(line => line.VariantId is not null && line.RemainingBase > 0).ToList();
        var line = candidates[random.Next(candidates.Count)];
        var quantity = random.Next(1, (int)line.RemainingBase + 1);

        await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            bill.SaleId,
            userId,
            shiftId,
            at,
            [new ReturnLineRequest(
                line.Id,
                Quantity.FromDecimal(quantity, line.Id),
                random.Next(4) == 0 ? ReturnDisposition.Damaged : ReturnDisposition.Sellable,
                Reasons[random.Next(Reasons.Length)])],
            random.Next(5) == 0 ? RefundMethod.Card : RefundMethod.Cash));

        line.RemainingBase -= quantity;
        bill.HasReturns = true;
    }

    private static async Task UnlinkedReturnAsync(
        SaleFixture fixture, Pool pool, Random random, long userId, long shiftId, DateTimeOffset at)
    {
        var product = pool.Products[random.Next(pool.Products.Count)];
        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(
            new OwnerOverrideRequest(ReturnPolicyAuditActions.UnlinkedReturn, "No receipt kept.", Owner, OwnerPassword));

        await fixture.Resolve<ICreateUnlinkedReturn>().CreateAsync(new CreateUnlinkedReturnCommand(
            userId,
            shiftId,
            at,
            [new UnlinkedReturnLineRequest(
                product.VariantId,
                Quantity.FromDecimal(random.Next(1, 4), pool.PieceUomId),
                Money.FromDecimal(Math.Round(product.Price * (random.Next(5, 11) / 10m), 2)),
                random.Next(3) == 0 ? ReturnDisposition.Damaged : ReturnDisposition.Sellable,
                "No receipt")],
            RefundMethod.Card,
            token,
            "No receipt kept, regular customer."));
    }

    private static async Task ChangeCostAsync(SaleFixture fixture, Pool pool, Random random, long userId, DateTimeOffset at)
    {
        // A real inbound posting: moves stock_balance.cost_avg and product.cost_avg, after which
        // every earlier sale's snapshot must still be what it was.
        var product = pool.Products[random.Next(pool.Products.Count)];
        var cost = Money.FromDecimal(random.Next(500, 20_000) / 100m);

        await fixture.Resolve<IStockLedger>().PostAsync(
            new StockPosting(
                product.VariantId, "GRN", Quantity.FromDecimal(100m, pool.PieceUomId), cost,
                "GRN", RefDocId: null, userId, at),
            CancellationToken.None);
    }

    private static async Task<List<object?[]>> ReadRowsAsync(SaleFixture fixture, string sql)
    {
        var connection = await fixture.OpenReadConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using DbDataReader reader = await command.ExecuteReaderAsync();

            var rows = new List<object?[]>();
            while (await reader.ReadAsync())
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }

                rows.Add(values);
            }

            return rows;
        }
    }

    // ---- Catalogue -----------------------------------------------------------------------------

    private sealed record PoolProduct(long VariantId, decimal Price, long? BoxUomId);

    private sealed record Pool(long PieceUomId, IReadOnlyList<PoolProduct> Products)
    {
        internal IEnumerable<long> VariantIds => Products.Select(product => product.VariantId);
    }

    private static Task<Pool> SeedPoolAsync(SaleFixture fixture)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();
        var seededAt = At(FirstDay.AddDays(-1), 8, 0);

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var piece = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var box = new Uom { Name = "Box of 12", Symbol = "box", DecimalPlaces = 0, Active = true };
            var catA = new Category { Name = "Hardware", Active = true };
            var catB = new Category { Name = "Power tools", Active = true };
            var brandX = new Brand { Name = "Acme", Active = true };
            var brandY = new Brand { Name = "Globex", Active = true };
            var taxes = new Dictionary<int, TaxClass>
            {
                [0] = new TaxClass { Name = "Rate 0", Rate = TaxRate.FromPercent(0m), Active = true },
                [5] = new TaxClass { Name = "Rate 5", Rate = TaxRate.FromPercent(5m), Active = true },
                [10] = new TaxClass { Name = "Rate 10", Rate = TaxRate.FromPercent(10m), Active = true },
                [15] = new TaxClass { Name = "Rate 15", Rate = TaxRate.FromPercent(15m), Active = true },
            };
            context.AddRange(box, catA, catB, brandX, brandY);
            context.AddRange(taxes.Values);
            await context.SaveChangesAsync(token);

            var products = new List<PoolProduct>();

            async Task AddAsync(string code, long? category, long? brand, int taxPercent, decimal price, decimal cost, bool boxed)
            {
                var product = new Product
                {
                    Code = code,
                    Name = code,
                    NameAlt = null,
                    CategoryId = category,
                    BrandId = brand,
                    BaseUomId = piece,
                    Type = "STANDARD",
                    TaxClassId = taxes[taxPercent].Id,
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
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt,
                };
                context.Add(product);
                await context.SaveChangesAsync(token);

                context.Add(new ProductUom
                {
                    ProductId = product.Id,
                    UomId = piece,
                    ConversionFactor = UomConversion.Base.ToScaled(),
                    SellingPrice = null,
                    IsBase = true,
                });

                if (boxed)
                {
                    context.Add(new ProductUom
                    {
                        ProductId = product.Id,
                        UomId = box.Id,
                        ConversionFactor = UomConversion.FromDecimal(12m).ToScaled(),
                        SellingPrice = Money.FromDecimal(price * 11m),
                        IsBase = false,
                    });
                }

                await context.SaveChangesAsync(token);

                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = code + "-A",
                    Attributes = "{}",
                    Price = Money.FromDecimal(price),
                    Active = true,
                    CreatedAt = seededAt,
                };
                context.Add(variant);
                await context.SaveChangesAsync(token);

                await ledger.PostAsync(
                    new StockPosting(
                        variant.Id, "OPENING", Quantity.FromDecimal(100_000m, piece), Money.FromDecimal(cost),
                        "OPENING", RefDocId: null, userId, seededAt),
                    token);

                products.Add(new PoolProduct(variant.Id, price, boxed ? box.Id : null));
            }

            await AddAsync("BOLT", catA.Id, brandX.Id, 10, 100.00m, 60.00m, boxed: false);
            await AddAsync("DRILL", catB.Id, brandY.Id, 15, 250.00m, 149.9999m, boxed: false);
            await AddAsync("NAIL", null, null, 0, 10.00m, 3.3333m, boxed: true);
            await AddAsync("SAW", catA.Id, brandY.Id, 5, 89.50m, 41.2537m, boxed: false);
            await AddAsync("PAINT", catB.Id, null, 10, 33.33m, 19.0476m, boxed: true);
            await AddAsync("GLUE", null, brandX.Id, 15, 7.45m, 2.7143m, boxed: false);
            await AddAsync("HAMMER", catA.Id, null, 0, 120.00m, 70.0001m, boxed: false);

            return new Pool(piece, products);
        });
    }
}
