using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Purchasing;
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
/// <b>AC-12 for the P3-T06 reports, generatively.</b> A seeded random 24-day trading history - bills with line and
/// bill discounts, open items, a multi-unit product, four tax rates (one class re-rated half way through), four tender
/// types, cancellations, linked and unlinked returns, goods receipts, adjustments and damage, a stock take, and one
/// shift closed every night - is checked after every day, while its shift is open and again once it is closed.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is asserted</b> (every expected figure is an independent scaled-integer recomputation written in this file
/// straight from <c>sale</c>, <c>sale_line</c>, <c>sale_return</c>, <c>sale_return_line</c>, <c>payment</c>,
/// <c>shift</c>, <c>goods_receipt</c> and <c>stock_movement</c>, never the report layer):
/// </para>
/// <list type="bullet">
/// <item>the tax report's per-rate rows add up to <c>SUM(sale.tax)</c> = <c>SUM(sale_line.tax)</c>, equal the
/// <c>GROUP BY sale_line.tax_rate</c> sums rate by rate, carry the returns' tax, and its taxable values add up to
/// <c>SUM(subtotal - bill_discount)</c> less the returns' subtotal - on the whole history and on a random sub-range;</item>
/// <item>the tender reconciliation's difference is zero, with nothing "not Z'd", for every range of closed days, and
/// while today's shift is open the whole of today's payments is the difference and its shift is the one "not Z'd";</item>
/// <item>every variant's stock card reconciles, every running balance is the ledger's <c>balance_after</c>, and its closing
/// balance is <c>stock_balance.qty_base</c>;</item>
/// <item>the variance history, supplier purchases, damage report and stock valuation equal their raw-table recomputations.</item>
/// </list>
/// <para>
/// A failure here is never a rounding artefact: every column is an exact scaled integer and no report rounds.
/// </para>
/// </remarks>
public sealed class StockCashGenerativeTests
{
    private const int Days = 24;
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";

    private static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);
    private static readonly DateOnly FirstDay = new(2026, 9, 6);

    private static readonly string[] Reasons = ["Changed mind", "Wrong size", "Faulty", "Duplicate purchase"];

    [Theory]
    [InlineData(false, 20260930)]
    [InlineData(true, 20261007)]
    public async Task AC_12_TaxTenderAndStockCardFiguresReconcileToTheRawTablesOnARandomTradingHistory(bool pricesIncludeTax, int seed)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.SeedReturnNumberSequenceAsync(fixture);
        await SalesReportDataset.DisableBackupOnShiftCloseAsync(fixture);
        await PricedVariantSeeder.UsePricingModeAsync(fixture, pricesIncludeTax);
        await fixture.Resolve<ISettings>().UpdateAsync(
            settings => settings with { Policy = settings.Policy with { AllowUnlinkedReturns = true } });

        var sequences = fixture.Resolve<INumberSequenceConfiguration>();
        await sequences.ConfigureAsync("GRN", "GRN-", "{prefix}{yyyy}-{n:000000}", 1);
        await sequences.ConfigureAsync("STOCK_TAKE", "ST-", "{prefix}{yyyy}-{n:000000}", 1);

        var session = fixture.Resolve<ISession>();
        var owner = session.CurrentUser!;
        var pool = await SeedPoolAsync(fixture);
        var supplier = await fixture.Resolve<ISupplierMaintenance>()
            .CreateAsync(new SaveSupplierCommand("Random Supplies", null, null, null, null, null));
        var random = new Random(seed);

        var bills = new List<SimBill>();
        var checks = 0;
        var grns = 0;
        var adjustments = 0;

        for (var dayIndex = 0; dayIndex < Days; dayIndex++)
        {
            var date = FirstDay.AddDays(dayIndex);

            if (dayIndex > 0)
            {
                await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(owner.Id, Money.Zero, At(date, 8, 0)));
            }

            var shiftId = session.ShiftId!.Value;
            var clock = At(date, 8, 30);

            if (dayIndex == Days / 2)
            {
                // Mid-history rate change: the class keeps its name, sells at 12% from now on.
                var classes = fixture.Resolve<ITaxClassMaintenance>();
                var rate10 = (await classes.ListAsync()).Single(tax => tax.Name == "Rate 10");
                await classes.UpdateAsync(rate10.Id, new SaveTaxClassCommand(rate10.Name, TaxRate.FromPercent(12m)));

                await StockTakeAsync(fixture, pool, random, At(date, 8, 5));
            }

            var operations = random.Next(12) == 0 ? 0 : random.Next(6, 13);

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

                if (roll < 55 || bills.Count == 0)
                {
                    bills.Add(await SellAsync(fixture, pool, random, owner.Id, shiftId, clock, date));
                }
                else if (roll < 62 && today.Count > 0)
                {
                    var victim = today[random.Next(today.Count)];
                    await fixture.Resolve<ICancelSale>().CancelAsync(new CancelSaleCommand(victim.SaleId, "Rung up by mistake", clock));
                    victim.Cancelled = true;
                }
                else if (roll < 78 && returnable.Count > 0)
                {
                    await LinkedReturnAsync(fixture, random, returnable[random.Next(returnable.Count)], owner.Id, shiftId, clock);
                }
                else if (roll < 84)
                {
                    await UnlinkedReturnAsync(fixture, pool, random, owner.Id, shiftId, clock);
                }
                else if (roll < 92)
                {
                    await ReceiveAsync(fixture, pool, random, supplier, clock);
                    grns++;
                }
                else
                {
                    await AdjustAsync(fixture, pool, random, clock);
                    adjustments++;
                }
            }

            // Shift still open: today's whole trading is "not Z'd", every earlier day is tied out.
            checks += await VerifyOpenDayAsync(fixture, date, dayIndex);

            if (dayIndex < Days - 1)
            {
                await fixture.Resolve<ICloseShift>().CloseAsync(
                    new CloseShiftCommand(shiftId, owner.Id, Money.Zero, At(date, 21, 0), Note: "reconciliation run"));

                checks += await VerifyClosedDaysAsync(fixture, random, date);
            }
        }

        // Close the last day too: now the whole history is Z'd and every report is checked one last time.
        var lastDate = FirstDay.AddDays(Days - 1);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(session.ShiftId!.Value, owner.Id, Money.Zero, At(lastDate, 21, 0), Note: "reconciliation run"));
        checks += await VerifyClosedDaysAsync(fixture, random, lastDate);
        await VerifyStockSideAsync(fixture, pool, random, lastDate);
        await VerifyShiftHistoryAsync(fixture, lastDate);

        // The run must actually have exercised everything it claims to.
        checks.Should().BeGreaterThan(80);
        grns.Should().BeGreaterThan(3);
        adjustments.Should().BeGreaterThan(3);
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'COMPLETED';")).Should().BeGreaterThan(60);
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'CANCELLED';")).Should().BeGreaterThan(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_return WHERE original_sale_id IS NULL;")).Should().BeGreaterThan(0);
        (await fixture.CountAsync("SELECT COUNT(DISTINCT tax_rate) FROM sale_line;")).Should().BeGreaterThanOrEqualTo(5, "0, 5, 10, 12 and 15 percent were all charged");
        (await fixture.CountAsync("SELECT COUNT(DISTINCT tender_type) FROM payment;")).Should().BeGreaterThanOrEqualTo(4);
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'STOCK_TAKE';")).Should().BeGreaterThan(0);
    }

    // ---- The checks ----------------------------------------------------------------------------

    private static async Task<int> VerifyOpenDayAsync(SaleFixture fixture, DateOnly today, int dayIndex)
    {
        var range = ReportDateRange.Custom(FirstDay, today);
        await VerifyTaxAsync(fixture, range, "open day " + dayIndex);

        var reconciliation = await fixture.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(range);
        var because = "[open day " + dayIndex + "]";

        reconciliation.IsTiedOut.Should().BeFalse("today's shift is open " + because);
        reconciliation.NotZd.Should().ContainSingle(item => item.Status == "OPEN", because);
        reconciliation.NotZd.Single(item => item.Status == "OPEN").BusinessDate.Should().Be(today, because);
        reconciliation.Shifts.Should().HaveCount(dayIndex, "every earlier day is closed " + because);

        // Today's whole net payments are the unexplained part: they are in the range and on no Z report.
        var todayNet = await NetPaymentsAsync(fixture, ReportDateRange.Custom(today, today));
        reconciliation.Difference.ToScaled().Should().Be(todayNet, because);
        reconciliation.RangeNetTotal.ToScaled().Should().Be(await NetPaymentsAsync(fixture, range), because);
        reconciliation.ByTender.Aggregate(Money.Zero, (sum, row) => sum + row.Difference).ToScaled().Should().Be(todayNet, because);
        reconciliation.NotZd.Single(item => item.Status == "OPEN").NetEffect.ToScaled().Should().Be(todayNet, because);
        reconciliation.HasUnexplainedDifference.Should().BeFalse("the open shift accounts for every difference " + because);

        return 2;
    }

    private static async Task<int> VerifyClosedDaysAsync(SaleFixture fixture, Random random, DateOnly today)
    {
        var span = today.DayNumber - FirstDay.DayNumber;
        var from = FirstDay.AddDays(random.Next(span + 1));
        var to = from.AddDays(random.Next(today.DayNumber - from.DayNumber + 1));

        foreach (var range in new[]
        {
            ReportDateRange.Custom(FirstDay, today),
            ReportDateRange.Custom(today, today),
            ReportDateRange.Custom(from, to),
        })
        {
            var label = "closed " + Text(range.From) + ".." + Text(range.To);
            await VerifyTaxAsync(fixture, range, label);

            var report = await fixture.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(range);

            report.NotZd.Should().BeEmpty(label);
            report.Difference.Should().Be(Money.Zero, label);
            report.ByTender.All(row => row.Difference == Money.Zero).Should().BeTrue(label + " every tender's difference is zero");
            report.IsTiedOut.Should().BeTrue(label);
            report.HasUnexplainedDifference.Should().BeFalse(label);
            report.ZNetTotal.ToScaled().Should().Be(await NetPaymentsAsync(fixture, range), label + " Z side equals the payments read raw");

            // AC-12: the till took exactly what the bills were worth, less what was paid back - bill totals and refunds.
            var billed = await fixture.CountAsync(
                "SELECT COALESCE(SUM(total), 0) FROM sale WHERE status = 'COMPLETED' AND business_date >= '" + Text(range.From)
                + "' AND business_date <= '" + Text(range.To) + "';");
            var refunded = await fixture.CountAsync(
                "SELECT COALESCE(SUM(total_refund), 0) FROM sale_return WHERE business_date >= '" + Text(range.From)
                + "' AND business_date <= '" + Text(range.To) + "';");
            report.RangeNetTotal.ToScaled().Should().Be(billed - refunded, label + " tenders == SUM(sale.total) - SUM(total_refund)");
            report.RangeNetTotal.Should().Be(report.ZNetTotal, label);
            report.Shifts.All(shift => shift.BusinessDate >= range.From && shift.BusinessDate <= range.To).Should().BeTrue(label);
            report.Shifts.Select(shift => shift.NetTotal).Aggregate(Money.Zero, (sum, net) => sum + net).Should().Be(report.ZNetTotal, label);
        }

        return 3;
    }

    private static async Task VerifyTaxAsync(SaleFixture fixture, ReportDateRange range, string label)
    {
        var report = await fixture.Resolve<ITaxReportQuery>().GetTaxReportAsync(range);
        var because = "[" + label + "]";

        var sales = "FROM sale WHERE status = 'COMPLETED' AND business_date >= '" + Text(range.From) + "' AND business_date <= '" + Text(range.To) + "'";
        var returns = "FROM sale_return WHERE business_date >= '" + Text(range.From) + "' AND business_date <= '" + Text(range.To) + "'";

        var headerTax = await fixture.CountAsync("SELECT COALESCE(SUM(tax), 0) " + sales + ";");
        var lineTax = await fixture.CountAsync(
            "SELECT COALESCE(SUM(sl.tax), 0) FROM sale_line sl WHERE sl.sale_id IN (SELECT id " + sales + ");");
        var returnTax = await fixture.CountAsync("SELECT COALESCE(SUM(tax), 0) " + returns + ";");
        var netRevenue = await fixture.CountAsync("SELECT COALESCE(SUM(subtotal - bill_discount), 0) " + sales + ";");
        var returnSubtotal = await fixture.CountAsync("SELECT COALESCE(SUM(subtotal), 0) " + returns + ";");

        lineTax.Should().Be(headerTax, because + " SUM(sale_line.tax) == SUM(sale.tax)");
        report.Rows.Sum(row => row.SalesTax.ToScaled()).Should().Be(lineTax, because + " per-rate tax == SUM(sale_line.tax)");
        report.Rows.Sum(row => row.SalesTax.ToScaled()).Should().Be(headerTax, because);
        report.Rows.Sum(row => row.ReturnsTax.ToScaled()).Should().Be(returnTax, because);
        report.Rows.Sum(row => row.SalesTaxable.ToScaled()).Should().Be(netRevenue, because + " taxable == SUM(subtotal - bill_discount)");
        report.Rows.Sum(row => row.ReturnsTaxable.ToScaled()).Should().Be(returnSubtotal, because);
        report.TotalNetTaxable.ToScaled().Should().Be(netRevenue - returnSubtotal, because);
        report.NetTax.ToScaled().Should().Be(headerTax - returnTax, because);
        report.SaleLineTaxTotal.ToScaled().Should().Be(lineTax, because);
        report.SaleHeaderTaxTotal.ToScaled().Should().Be(headerTax, because);
        report.ReturnHeaderTaxTotal.ToScaled().Should().Be(returnTax, because);
        report.IsReconciled.Should().BeTrue(because);

        // Rate by rate, from the lines' own snapshot and (for returns) the line reversed.
        var bySnapshot = await ReadRowsAsync(
            fixture,
            "SELECT sl.tax_rate, SUM(sl.tax) FROM sale_line sl JOIN sale s ON s.id = sl.sale_id WHERE s.status = 'COMPLETED' "
            + "AND s.business_date >= '" + Text(range.From) + "' AND s.business_date <= '" + Text(range.To) + "' GROUP BY sl.tax_rate;");
        foreach (var row in bySnapshot)
        {
            var rate = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
            var tax = Convert.ToInt64(row[1], CultureInfo.InvariantCulture);

            report.Rows.Where(r => r.Kind == TaxReportRowKind.Rate && r.Rate!.Value.ToScaled() == rate)
                .Should().ContainSingle(because + " one row per rate").Which.SalesTax.ToScaled().Should().Be(tax, because + " rate " + rate);
        }

        var returnsByRate = await ReadRowsAsync(
            fixture,
            "SELECT sl.tax_rate, SUM(srl.tax), SUM(srl.line_refund) FROM sale_return_line srl JOIN sale_return sr ON sr.id = srl.sale_return_id "
            + "JOIN sale_line sl ON sl.id = srl.sale_line_id WHERE sr.business_date >= '" + Text(range.From)
            + "' AND sr.business_date <= '" + Text(range.To) + "' GROUP BY sl.tax_rate;");
        foreach (var row in returnsByRate)
        {
            var rate = Convert.ToInt64(row[0], CultureInfo.InvariantCulture);
            var rateRow = report.Rows.Single(r => r.Kind == TaxReportRowKind.Rate && r.Rate!.Value.ToScaled() == rate);

            rateRow.ReturnsTax.ToScaled().Should().Be(Convert.ToInt64(row[1], CultureInfo.InvariantCulture), because + " returns at rate " + rate);
            rateRow.ReturnsTaxable.ToScaled().Should().Be(Convert.ToInt64(row[2], CultureInfo.InvariantCulture), because + " returns at rate " + rate);
        }

        var unlinkedTax = await fixture.CountAsync(
            "SELECT COALESCE(SUM(srl.tax), 0) FROM sale_return_line srl JOIN sale_return sr ON sr.id = srl.sale_return_id "
            + "WHERE srl.sale_line_id IS NULL AND sr.business_date >= '" + Text(range.From) + "' AND sr.business_date <= '" + Text(range.To) + "';");
        report.Rows.Where(r => r.Kind == TaxReportRowKind.UnlinkedReturns).Sum(r => r.ReturnsTax.ToScaled()).Should().Be(unlinkedTax, because);
    }

    private static async Task VerifyStockSideAsync(SaleFixture fixture, Pool pool, Random random, DateOnly lastDay)
    {
        var stockCards = fixture.Resolve<IStockCardQuery>();
        var whole = ReportDateRange.Custom(FirstDay.AddDays(-30), lastDay.AddDays(1));

        foreach (var variantId in pool.VariantIds)
        {
            var id = variantId.ToString(CultureInfo.InvariantCulture);
            var because = "[variant " + id + "]";
            var card = await stockCards.GetStockCardAsync(variantId, whole);
            var stored = await fixture.CountAsync("SELECT qty_base FROM stock_balance WHERE product_variant_id = " + id + ";");
            var ledgerRows = await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE product_variant_id = " + id + ";");
            var ledgerSum = await fixture.CountAsync("SELECT COALESCE(SUM(qty_base), 0) FROM stock_movement WHERE product_variant_id = " + id + ";");

            card!.Reconciles.Should().BeTrue(because);
            card.Rows.Should().HaveCount((int)ledgerRows, because);
            card.Rows.All(row => row.MatchesLedger).Should().BeTrue(because);
            card.Rows.Select(row => row.MovementId).Should().BeInAscendingOrder(because);
            card.OpeningBalance.ToScaled().Should().Be(0, because);
            card.ClosingBalance.ToScaled().Should().Be(stored, because + " closing == stock_balance.qty_base");
            card.TotalIn.ToScaled().Should().Be(card.Rows.Where(row => row.QtyBase.ToScaled() >= 0).Sum(row => row.QtyBase.ToScaled()), because);
            (card.TotalIn.ToScaled() + card.TotalOut.ToScaled()).Should().Be(ledgerSum, because + " in + out == SUM(qty_base)");
            ledgerSum.Should().Be(stored, because + " the projection equals the ledger sum");

            // A random sub-range: the opening balance is read, the rows carry on from it, and it still closes where the ledger did.
            var from = FirstDay.AddDays(random.Next(Days));
            var to = from.AddDays(random.Next(Days - (from.DayNumber - FirstDay.DayNumber)));
            var part = await stockCards.GetStockCardAsync(variantId, ReportDateRange.Custom(from, to));
            var partBecause = because + " " + Text(from) + ".." + Text(to);

            part!.Reconciles.Should().BeTrue(partBecause);
            part.Rows.All(row => row.MatchesLedger).Should().BeTrue(partBecause);
            (part.OpeningBalance.ToScaled() + part.TotalIn.ToScaled() + part.TotalOut.ToScaled()).Should().Be(part.ClosingBalance.ToScaled(), partBecause);

            if (part.Rows.Count > 0)
            {
                part.ClosingBalance.Should().Be(part.Rows[^1].BalanceAfter, partBecause);
                part.Rows.Sum(row => row.QtyBase.ToScaled()).Should().Be(part.TotalIn.ToScaled() + part.TotalOut.ToScaled(), partBecause);
            }
        }

        // The stock valuation is the scaled product over the scaled sum, exactly.
        var valuation = await fixture.Resolve<IStockValuationQuery>().GetValuationAsync();
        var scaled = await fixture.CountAsync("SELECT COALESCE(SUM(qty_base * cost_avg), 0) FROM stock_balance;");
        valuation.TotalValue.Amount.Should().Be(scaled / 100_000_000m);
        valuation.Lines.Aggregate(Money.Zero, (sum, line) => sum + line.Value).Should().Be(valuation.TotalValue);

        // Supplier purchases: suppliers, items and the receipt headers agree exactly.
        var purchases = await fixture.Resolve<ISupplierPurchaseReportQuery>().GetReportAsync(whole);
        purchases.TotalValue.ToScaled().Should().Be(await fixture.CountAsync("SELECT COALESCE(SUM(subtotal + other_cost), 0) FROM goods_receipt;"));
        purchases.TotalTax.ToScaled().Should().Be(await fixture.CountAsync("SELECT COALESCE(SUM(tax), 0) FROM goods_receipt;"));
        purchases.TotalPurchases.ToScaled().Should().Be(await fixture.CountAsync("SELECT COALESCE(SUM(total), 0) FROM goods_receipt;"));
        purchases.ByItem.Aggregate(Money.Zero, (sum, row) => sum + row.Value).Should().Be(purchases.TotalValue, "item values add up to supplier values");
        purchases.BySupplier.Sum(row => row.ReceiptCount).Should().Be((int)await fixture.CountAsync("SELECT COUNT(*) FROM goods_receipt;"));

        // Damage and adjustments: each ledger movement and each damaged return line, at its recorded cost.
        var damage = await fixture.Resolve<IDamageAdjustmentReportQuery>().GetReportAsync(whole);
        var movementRows = await ReadRowsAsync(
            fixture, "SELECT qty_base, unit_cost FROM stock_movement WHERE movement_type IN ('ADJUSTMENT', 'DAMAGE');");
        var returnRows = await ReadRowsAsync(
            fixture, "SELECT qty_base, unit_cost FROM sale_return_line WHERE disposition = 'DAMAGED';");

        var expectedNet = movementRows.Sum(row => Convert.ToInt64(row[0], CultureInfo.InvariantCulture) / 10_000m * (Convert.ToInt64(row[1], CultureInfo.InvariantCulture) / 10_000m))
            - returnRows.Sum(row => Convert.ToInt64(row[0], CultureInfo.InvariantCulture) / 10_000m * (Convert.ToInt64(row[1], CultureInfo.InvariantCulture) / 10_000m));
        damage.NetValue.Amount.Should().Be(expectedNet);
        damage.Rows.Sum(row => row.Count).Should().Be(movementRows.Count + returnRows.Count);
        damage.NetValue.Should().Be(damage.TotalGain - damage.TotalLoss);
    }

    private static async Task VerifyShiftHistoryAsync(SaleFixture fixture, DateOnly lastDay)
    {
        var history = await fixture.Resolve<IShiftVarianceHistoryQuery>().GetHistoryAsync(ReportDateRange.Custom(FirstDay, lastDay));

        history.Rows.Should().HaveCount(Days);
        history.NetVariance.ToScaled().Should().Be(await fixture.CountAsync("SELECT COALESCE(SUM(variance), 0) FROM shift WHERE status = 'CLOSED';"));
        history.Rows.Select(row => row.CumulativeVariance).Last().Should().Be(history.NetVariance);
        (history.TotalOver + history.TotalShort).Should().Be(history.NetVariance);
        history.Rows.Select(row => row.BusinessDate).Should().BeInAscendingOrder();
        history.Trend.Should().NotBe(VarianceTrend.NotEnoughData, "24 shifts");
    }

    private static async Task<long> NetPaymentsAsync(SaleFixture fixture, ReportDateRange range)
    {
        var sales = "SELECT id FROM sale WHERE status = 'COMPLETED' AND business_date >= '" + Text(range.From)
            + "' AND business_date <= '" + Text(range.To) + "'";
        var returns = "SELECT id FROM sale_return WHERE business_date >= '" + Text(range.From)
            + "' AND business_date <= '" + Text(range.To) + "'";

        return await fixture.CountAsync(
            "SELECT COALESCE(SUM(amount), 0) FROM payment WHERE sale_id IN (" + sales + ") OR sale_return_id IN (" + returns + ");");
    }

    // ---- The random history --------------------------------------------------------------------

    private static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new(date.Year, date.Month, date.Day, hour, minute, 0, Offset);

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
            var first = Math.Round(total * random.Next(1, 10) / 10m, 2);
            tenders.Add(new TenderRequest(TenderTypes.Cash, Money.FromDecimal(first)));
            if (total - first > 0m)
            {
                tenders.Add(new TenderRequest(
                    random.Next(2) == 0 ? TenderTypes.Card : TenderTypes.BankTransfer, Money.FromDecimal(total - first), "REF-" + random.Next(1000, 9999)));
            }
        }
        else
        {
            var type = random.Next(4) switch
            {
                0 => TenderTypes.Cash,
                1 => TenderTypes.Card,
                2 => TenderTypes.BankTransfer,
                _ => TenderTypes.Cheque,
            };
            tenders.Add(new TenderRequest(type, quote.Total, "REF-" + random.Next(1000, 9999)));
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

    private static async Task ReceiveAsync(SaleFixture fixture, Pool pool, Random random, long supplier, DateTimeOffset at)
    {
        var lines = new List<CreateGoodsReceiptLineCommand>();
        foreach (var product in pool.Products.OrderBy(_ => random.Next()).Take(random.Next(1, 4)))
        {
            var quantity = random.Next(5, 80);
            var cost = Money.FromDecimal(random.Next(300, 9000) / 100m);
            var tax = random.Next(2) == 0 ? Money.FromDecimal(random.Next(0, 4000) / 100m) : (Money?)null;
            lines.Add(new CreateGoodsReceiptLineCommand(product.VariantId, pool.PieceUomId, quantity, cost, tax));
        }

        await fixture.Resolve<IGoodsReceiptService>().ReceiveAsync(new CreateGoodsReceiptCommand(
            supplier,
            null,
            null,
            at,
            random.Next(3) == 0 ? Money.FromDecimal(random.Next(100, 2500) / 100m) : Money.Zero,
            null,
            lines));
    }

    private static async Task AdjustAsync(SaleFixture fixture, Pool pool, Random random, DateTimeOffset at)
    {
        var product = pool.Products[random.Next(pool.Products.Count)];
        var adjust = fixture.Resolve<IPostAdjustment>();

        switch (random.Next(3))
        {
            case 0:
                await adjust.WriteOffDamageAsync(new DamageCommand(product.VariantId, random.Next(1, 6), "Dropped in the yard", at));
                break;
            case 1:
                await adjust.AdjustAsync(new AdjustmentCommand(product.VariantId, random.Next(1, 6), null, "Found on the wrong shelf", at));
                break;
            default:
                await adjust.AdjustAsync(new AdjustmentCommand(product.VariantId, -random.Next(1, 6), null, "Count correction", at));
                break;
        }
    }

    private static async Task StockTakeAsync(SaleFixture fixture, Pool pool, Random random, DateTimeOffset at)
    {
        var stockTakes = fixture.Resolve<IStockTakeService>();
        var started = await stockTakes.StartAsync(new StartStockTakeCommand("ALL", at));

        foreach (var variantId in pool.VariantIds)
        {
            var system = await fixture.CountAsync(
                "SELECT qty_base FROM stock_balance WHERE product_variant_id = " + variantId.ToString(CultureInfo.InvariantCulture) + ";");
            var counted = system / 10_000m - random.Next(0, 4);

            await stockTakes.RecordCountAsync(new RecordStockTakeCountCommand(started.StockTakeId, variantId, counted, at.AddMinutes(5)));
        }

        await stockTakes.PostAsync(new PostStockTakeCommand(started.StockTakeId, at.AddMinutes(10)));
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
            var taxes = new Dictionary<int, TaxClass>
            {
                [0] = new TaxClass { Name = "Rate 0", Rate = TaxRate.FromPercent(0m), Active = true },
                [5] = new TaxClass { Name = "Rate 5", Rate = TaxRate.FromPercent(5m), Active = true },
                [10] = new TaxClass { Name = "Rate 10", Rate = TaxRate.FromPercent(10m), Active = true },
                [15] = new TaxClass { Name = "Rate 15", Rate = TaxRate.FromPercent(15m), Active = true },
            };
            context.Add(box);
            context.AddRange(taxes.Values);
            await context.SaveChangesAsync(token);

            var products = new List<PoolProduct>();

            async Task AddAsync(string code, int taxPercent, decimal price, decimal cost, bool boxed)
            {
                var product = new Product
                {
                    Code = code,
                    Name = code,
                    NameAlt = null,
                    CategoryId = null,
                    BrandId = null,
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

            await AddAsync("BOLT", 10, 100.00m, 60.00m, boxed: false);
            await AddAsync("DRILL", 15, 250.00m, 149.9999m, boxed: false);
            await AddAsync("NAIL", 0, 10.00m, 3.3333m, boxed: true);
            await AddAsync("SAW", 5, 89.50m, 41.2537m, boxed: false);
            await AddAsync("PAINT", 10, 33.33m, 19.0476m, boxed: true);
            await AddAsync("GLUE", 15, 7.45m, 2.7143m, boxed: false);
            await AddAsync("HAMMER", 0, 120.00m, 70.0001m, boxed: false);

            return new Pool(piece, products);
        });
    }
}
