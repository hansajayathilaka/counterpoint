using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-16, the supplier purchase summary (task P3-T06 "Do this" #5), against the four goods receipts in
/// <see cref="StockCashDataset"/>. Value is landed cost excluding tax (<c>line_total - tax</c>, freight share in);
/// hand figures:
/// </summary>
/// <remarks>
/// <code>
/// G1 Sep 3        Acme   Bolt 100 @ 54.52 = 5452.00 (+545.20 tax); Drill 10 @ 99.65 = 996.50 (+99.65 tax)
///                        value 6448.50  tax 644.85  total 7093.35
/// G2 Sep 10       Zenith Washer 100 @ 2.00 (+20.00 tax) and Gasket 10 @ 10.00 (+10.00 tax), freight 15.00 shared 10.00 / 5.00
///                        Washer landed 210.00 (2.10 each), Gasket landed 105.00 (10.50 each)
///                        value 315.00  tax 30.00  total 345.00
/// G3 Sep 12 23:59:59 Zenith Washer 100 @ 3.20 = 320.00, no tax      value 320.00  total 320.00
/// G4 Sep 13 00:00:00 Acme   Bolt 50 @ 65.18 = 3259.00, no tax       value 3259.00 total 3259.00
/// By supplier   Acme   2 receipts  value 9707.50  tax 644.85  total 10352.35
///               Zenith 2 receipts  value  635.00  tax  30.00  total   665.00
/// By item       Bolt   150 pcs  value 8711.00  average 8711/150  first 54.52  last 65.18  change +10.66
///               Drill   10 pcs  value  996.50  average 99.65
///               Washer 200 pcs  value  530.00  average 2.65      first 2.10   last 3.20   change +1.10
///               Gasket  10 pcs  value  105.00  average 10.50
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class SupplierPurchaseReportTests(StockCashReportFixture fixture)
{
    private static readonly ReportDateRange September = ReportDateRange.Custom(new(2026, 9, 1), new(2026, 9, 30));

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private ISupplierPurchaseReportQuery Query => fixture.Host.Resolve<ISupplierPurchaseReportQuery>();

    [Fact]
    public async Task RPT_16_PurchasesBySupplierSumTheReceiptHeadersLandedValueTaxAndTotal()
    {
        var report = await Query.GetReportAsync(September);

        report.BySupplier.Select(row => (row.SupplierName, row.ReceiptCount, row.Value, row.Tax, row.Total)).Should().Equal(
        [
            ("Acme Fasteners", 2, M(9707.50m), M(644.85m), M(10352.35m)),
            ("Zenith Tools", 2, M(635.00m), M(30.00m), M(665.00m)),
        ]);
        report.BySupplier[0].SupplierId.Should().Be(fixture.Data.AcmeId);
        report.BySupplier[1].SupplierId.Should().Be(fixture.Data.ZenithId);

        report.TotalValue.Should().Be(M(10342.50m));
        report.TotalTax.Should().Be(M(674.85m));
        report.TotalPurchases.Should().Be(M(11017.35m));
        report.SupplierId.Should().BeNull();

        // Independent: the receipt headers - value is subtotal + freight, total adds the tax.
        (await fixture.Host.CountAsync("SELECT SUM(subtotal + other_cost) FROM goods_receipt;")).Should().Be(report.TotalValue.ToScaled());
        (await fixture.Host.CountAsync("SELECT SUM(total) FROM goods_receipt;")).Should().Be(report.TotalPurchases.ToScaled());
    }

    [Fact]
    public async Task RPT_16_FreightIsSharedIntoTheLinesAndTaxIsKeptOutOfTheLandedValue()
    {
        var report = await Query.GetReportAsync(September);

        var washer = report.ByItem.Single(row => row.Sku == "EXT-WASHER-A");
        var gasket = report.ByItem.Single(row => row.Sku == "EXT-GASKET-A");

        // G2's 15.00 freight shared 10.00 / 5.00 over subtotals 200.00 / 100.00 (2:1).
        gasket.Value.Should().Be(M(105.00m), "100.00 + its 5.00 freight share, excluding the 10.00 tax");
        gasket.AverageUnitCost.Should().Be(M(10.50m));
        gasket.FirstUnitCost.Should().Be(M(10.50m));
        washer.FirstUnitCost.Should().Be(M(2.10m), "G2's landed cost: (200.00 + 10.00) / 100");
    }

    [Fact]
    public async Task RPT_16_PurchasesByItemCarryQuantityLandedValueWeightedAverageCostAndTheCostChange()
    {
        var report = await Query.GetReportAsync(September);

        report.ByItem.Select(row => row.Sku).Should().Equal(
            ["RPT-BOLT-A", "RPT-DRILL-A", "EXT-WASHER-A", "EXT-GASKET-A"], "largest landed value first");

        var bolt = report.ByItem[0];
        bolt.Description.Should().Be("Bolt");
        bolt.QtyBase.Value.Should().Be(150m, "100 + 50");
        bolt.Value.Should().Be(M(8711.00m), "5452.00 + 3259.00");
        bolt.AverageUnitCost.Should().Be(M(8711.00m / 150m), "a weighted average - value over quantity - not the mean of 54.52 and 65.18 (59.85)");
        bolt.FirstUnitCost.Should().Be(M(54.52m));
        bolt.LastUnitCost.Should().Be(M(65.18m));
        bolt.CostChange.Should().Be(M(10.66m));
        bolt.CostChangeRate.Should().Be(10.66m / 54.52m);

        var drill = report.ByItem[1];
        drill.QtyBase.Value.Should().Be(10m);
        drill.Value.Should().Be(M(996.50m));
        drill.AverageUnitCost.Should().Be(M(99.65m));
        drill.CostChange.Should().Be(Money.Zero);
        drill.CostChangeRate.Should().Be(0m);

        var washer = report.ByItem[2];
        washer.QtyBase.Value.Should().Be(200m);
        washer.Value.Should().Be(M(530.00m), "210.00 + 320.00");
        washer.AverageUnitCost.Should().Be(M(2.65m));
        washer.FirstUnitCost.Should().Be(M(2.10m));
        washer.LastUnitCost.Should().Be(M(3.20m));
        washer.CostChange.Should().Be(M(1.10m));
        washer.CostChangeRate.Should().Be(1.10m / 2.10m);

        report.ByItem.Aggregate(Money.Zero, (sum, row) => sum + row.Value).Should().Be(report.TotalValue, "supplier and item values add up to the same figure");
    }

    [Fact]
    public async Task RPT_16_TheCostMovementSeriesListsOnlyItemsWhoseLandedCostChangedInReceiptOrder()
    {
        var report = await Query.GetReportAsync(September);

        report.CostMovement.Select(point => (point.Sku, point.GrnNo, point.SupplierName, point.UnitCostBase)).Should().Equal(
        [
            ("EXT-WASHER-A", StockCashDataset.Grn2, "Zenith Tools", M(2.10m)),
            ("EXT-WASHER-A", StockCashDataset.Grn3, "Zenith Tools", M(3.20m)),
            ("RPT-BOLT-A", StockCashDataset.Grn1, "Acme Fasteners", M(54.52m)),
            ("RPT-BOLT-A", StockCashDataset.Grn4, "Acme Fasteners", M(65.18m)),
        ]);
        report.CostMovement.Select(point => point.ReceivedAt).Should().Equal(
            StockCashDataset.At(10, 9, 0), StockCashDataset.At(12, 23, 59, 59), StockCashDataset.At(3, 10, 0), StockCashDataset.At(13, 0, 0));
        report.CostMovement.Should().NotContain(point => point.Sku == "RPT-DRILL-A" || point.Sku == "EXT-GASKET-A", "their cost never changed");
    }

    [Fact]
    public async Task RPT_16_ASupplierFilterNarrowsEveryTableAndTheTotals()
    {
        var acme = await Query.GetReportAsync(September, fixture.Data.AcmeId);

        acme.SupplierId.Should().Be(fixture.Data.AcmeId);
        acme.BySupplier.Select(row => row.SupplierName).Should().Equal("Acme Fasteners");
        acme.ByItem.Select(row => row.Sku).Should().Equal("RPT-BOLT-A", "RPT-DRILL-A");
        acme.CostMovement.Select(point => point.Sku).Should().OnlyContain(sku => sku == "RPT-BOLT-A");
        acme.TotalValue.Should().Be(M(9707.50m));
        acme.TotalTax.Should().Be(M(644.85m));
        acme.TotalPurchases.Should().Be(M(10352.35m));

        var zenith = await Query.GetReportAsync(September, fixture.Data.ZenithId);

        zenith.BySupplier.Select(row => row.SupplierName).Should().Equal("Zenith Tools");
        zenith.ByItem.Select(row => row.Sku).Should().Equal("EXT-WASHER-A", "EXT-GASKET-A");
        zenith.CostMovement.Should().HaveCount(2);
        zenith.TotalValue.Should().Be(M(635.00m));
        zenith.TotalPurchases.Should().Be(M(665.00m));
    }

    [Fact]
    public async Task RPT_16_AReceiptIsDatedByItsReceivedDayATwentyThreeFiftyNineReceiptIsInAndAMidnightOneIsOut()
    {
        // G3 is Sep 12 23:59:59, G4 is Sep 13 00:00:00.
        var throughTwelfth = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 10), new(2026, 9, 12)));
        throughTwelfth.BySupplier.Select(row => (row.SupplierName, row.ReceiptCount)).Should().Equal([("Zenith Tools", 2)]);
        throughTwelfth.TotalValue.Should().Be(M(635.00m));

        var thirteenth = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 13), new(2026, 9, 13)));
        thirteenth.BySupplier.Select(row => (row.SupplierName, row.ReceiptCount)).Should().Equal([("Acme Fasteners", 1)]);
        thirteenth.TotalValue.Should().Be(M(3259.00m));
        thirteenth.ByItem.Should().ContainSingle();
        thirteenth.CostMovement.Should().BeEmpty("one receipt in the range cannot show a change");

        var backdated = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 3), new(2026, 9, 3)));
        backdated.BySupplier.Select(row => row.SupplierName).Should().Equal("Acme Fasteners");
        backdated.TotalPurchases.Should().Be(M(7093.35m), "G1 was entered after the Sep 6-7 trading but received on Sep 3");
    }

    [Fact]
    public async Task RPT_16_ARangeWithNoReceiptsIsEmptyWithZeroTotals()
    {
        var report = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)));

        report.BySupplier.Should().BeEmpty();
        report.ByItem.Should().BeEmpty();
        report.CostMovement.Should().BeEmpty();
        report.TotalValue.Should().Be(Money.Zero);
        report.TotalTax.Should().Be(Money.Zero);
        report.TotalPurchases.Should().Be(Money.Zero);
    }
}
