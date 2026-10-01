using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-02, sales by item, category and brand (task P3-T05 "Do this" #2), checked against the
/// hand-worked history in <see cref="SalesReportDataset"/>. A row's net is its lines' charge less
/// its share of the bill discount less the pre-tax refund of what came back.
/// </summary>
/// <remarks>
/// Net by product: Bolt 256.50 (B1) + 200.00 (B3) + 100.00 (B6) - 85.50 (R1) - 100.00 (R3) = 371.00;
/// Drill 237.50 (B1) + 500.00 (B5) - 250.00 (R2) = 487.50; Nail 200.00 + 50.00 = 250.00;
/// the open-item Delivery 50.00. Together 1158.50, the canonical net sales.
/// </remarks>
[Collection(SalesReportFixture.Name)]
public sealed class SalesBreakdownReportTests(SalesReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    [Fact]
    public async Task RPT_02_ByItemRowsAreRankedAndMatchTheHandWorkedFigures()
    {
        var data = fixture.Data;

        var report = await fixture.Host.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(BothDays, SalesBreakdownDimension.Item);

        report.Dimension.Should().Be(SalesBreakdownDimension.Item);
        report.TotalNet.Should().Be(M(1158.50m));
        report.Rows.Select(row => row.Name).Should().Equal(["Drill", "Bolt", "Nail", "(Open items)"], "ranked by net, best first");
        report.Rows.Select(row => row.Rank).Should().Equal([1, 2, 3, 4]);

        var drill = report.Rows[0];
        drill.Key.Should().Be(data.DrillVariantId);
        drill.Code.Should().Be("RPT-DRILL-A");
        drill.QtyBase.Value.Should().Be(2m, "3 sold - 1 returned");
        drill.UomSymbol.Should().Be("pc");
        drill.Net.Should().Be(M(487.50m));
        drill.ShareOfNet.Should().BeApproximately(487.50m / 1158.50m, 0.0000001m);

        var bolt = report.Rows[1];
        bolt.Key.Should().Be(data.BoltVariantId);
        bolt.Code.Should().Be("RPT-BOLT-A");
        bolt.QtyBase.Value.Should().Be(4m, "6 sold - 2 returned");
        bolt.Net.Should().Be(M(371.00m));
        bolt.ShareOfNet.Should().BeApproximately(371.00m / 1158.50m, 0.0000001m);

        var nail = report.Rows[2];
        nail.Key.Should().Be(data.NailVariantId);
        nail.QtyBase.Value.Should().Be(29m, "2 boxes of 12 = 24 pieces, plus 5 pieces - quantity is in base units");
        nail.Net.Should().Be(M(250.00m));

        var open = report.Rows[3];
        open.Key.Should().BeNull("an open item has no variant");
        open.Code.Should().BeEmpty();
        open.UomSymbol.Should().BeNull();
        open.QtyBase.Value.Should().Be(1m);
        open.Net.Should().Be(M(50.00m));
        open.ShareOfNet.Should().BeApproximately(50.00m / 1158.50m, 0.0000001m);

        report.Rows.Aggregate(0m, (sum, row) => sum + row.ShareOfNet).Should().BeApproximately(1m, 0.0000001m);
    }

    [Fact]
    public async Task RPT_02_ByCategoryFilesAProductWithNoCategoryAndAnOpenItemTogether()
    {
        var data = fixture.Data;

        var report = await fixture.Host.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(BothDays, SalesBreakdownDimension.Category);

        report.TotalNet.Should().Be(M(1158.50m));
        report.Rows.Select(row => row.Name).Should().Equal(["Tools", "Fasteners", "(No category)"]);

        report.Rows[0].Key.Should().Be(data.ToolsCategoryId);
        report.Rows[0].Net.Should().Be(M(487.50m));
        report.Rows[0].UomSymbol.Should().BeNull("a category mixes units");
        report.Rows[0].Code.Should().BeEmpty();

        report.Rows[1].Key.Should().Be(data.FastenersCategoryId);
        report.Rows[1].Net.Should().Be(M(371.00m));

        report.Rows[2].Key.Should().BeNull();
        report.Rows[2].Net.Should().Be(M(300.00m), "Nail 250.00 + the open-item Delivery 50.00");
        report.Rows[2].ShareOfNet.Should().BeApproximately(300.00m / 1158.50m, 0.0000001m);
    }

    [Fact]
    public async Task RPT_02_ByBrandFilesAProductWithNoBrandAndAnOpenItemTogether()
    {
        var data = fixture.Data;

        var report = await fixture.Host.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(BothDays, SalesBreakdownDimension.Brand);

        report.TotalNet.Should().Be(M(1158.50m));
        report.Rows.Select(row => row.Name).Should().Equal(["Makita", "Bosch", "(No brand)"]);
        report.Rows[0].Key.Should().Be(data.MakitaBrandId);
        report.Rows[0].Net.Should().Be(M(487.50m));
        report.Rows[1].Key.Should().Be(data.BoschBrandId);
        report.Rows[1].Net.Should().Be(M(371.00m));
        report.Rows[2].Key.Should().BeNull();
        report.Rows[2].Net.Should().Be(M(300.00m));
    }

    [Fact]
    public async Task RPT_02_ADayOnlyRangeUsesOnlyThatDaysLinesAndThatDaysReturns()
    {
        var report = await fixture.Host.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(
            ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo), SalesBreakdownDimension.Item);

        // Day two: Drill 500.00 - 250.00 (R2); Bolt 100.00 (B6) - 85.50 (R1) - 100.00 (R3); Nail 50.00.
        report.Rows.Select(row => (row.Name, row.Net)).Should().Equal(
            [("Drill", M(250.00m)), ("Nail", M(50.00m)), ("Bolt", M(-85.50m))],
            "a return dated in the range nets against sales even when its bill was on an earlier day");
        report.TotalNet.Should().Be(M(214.50m), "the canonical net of day two");
    }

    [Fact]
    public async Task RPT_02_AnEmptyRangeHasNoRowsAndAZeroTotalWithNoDivisionByZero()
    {
        var report = await fixture.Host.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(
            ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)), SalesBreakdownDimension.Item);

        report.Rows.Should().BeEmpty();
        report.TotalNet.Should().Be(Money.Zero);
    }
}
