using System;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Shifts;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;
using VerifyXunit;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The fixed-layout bridge from an X report snapshot to a byte stream (SRS FR-7.1, FR-8.3,
/// RPT-04, task P3-T02 "Do this" #2).
/// </summary>
public sealed class EscPosXReportRendererTests
{
    [Fact]
    public void FR_8_3_TheShiftHeaderAndFiguresReachThePaper()
    {
        var lines = Print(Report());

        lines.Should().Contain(line => line.Contains("X REPORT", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("SH-000001", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Priya", StringComparison.Ordinal));

        // 2 sales worth 330.00, 1 return worth 110.00 - see Report() below.
        lines.Should().Contain(line => line.TrimEnd().EndsWith("330.00", StringComparison.Ordinal));
        lines.Should().Contain(line => line.TrimEnd().EndsWith("110.00", StringComparison.Ordinal));
    }

    [Fact]
    public void ATaxBreakdownLineReachesThePaperForEachRate()
    {
        var lines = Print(Report());

        lines.Should().Contain(
            line => line.Contains("10%", StringComparison.Ordinal) && line.TrimEnd().EndsWith("30.00", StringComparison.Ordinal),
            "FR-8.3: the X report content includes a tax breakdown");
    }

    [Fact]
    public void TenderTypesAreBrokenOutSalesAndRefundsSeparately()
    {
        var lines = Print(Report());

        lines.Should().Contain(line => line.Contains("CASH sales", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("CASH refunds", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("CARD sales", StringComparison.Ordinal));

        // No CARD refunds this shift (RefundsAmount is zero) - no line printed about one, the
        // same "no fee, no line" discipline EscPosReturnReceiptRendererTests already checks for a
        // restocking fee.
        lines.Should().NotContain(line => line.Contains("CARD refunds", StringComparison.Ordinal));
    }

    [Fact]
    public void TheExpectedDrawerFigureReachesThePaper()
    {
        var lines = Print(Report());

        lines.Should().Contain(line => line.Contains("Expected cash", StringComparison.Ordinal)
            && line.TrimEnd().EndsWith("1110.00", StringComparison.Ordinal));
    }

    [Fact]
    public void ItNeverKicksTheDrawer()
    {
        // ESC p 0 25 250 - the same drawer-kick sequence the sale receipt renderer emits for a
        // cash tender. An X report only reads what the drawer should hold; it is not a cash event.
        var kick = new byte[] { 0x1B, 0x70, 0x00, 25, 250 };

        FindSequence(Render(Report()), kick).Should().Be(-1, "an X report never opens the drawer");
    }

    [Fact]
    public void TheReceiptEndsWithACut()
    {
        // GS V 1 - partial cut, PrinterCapabilities' default.
        FindSequence(Render(Report()), [0x1D, 0x56, 1]).Should().BeGreaterThan(-1);
    }

    [Fact]
    public Task Srs_10_1_TheXReportRendersToTheCommittedByteStream()
    {
        var bytes = Render(Report());

        return Verifier.Verify(EscPosDump.Describe(bytes)).UseDirectory("Snapshots");
    }

    private static byte[] Render(XReportSummary report) =>
        new EscPosXReportRenderer(new EscPosRenderer(), new HalfAwayFromZeroRounding(decimalPlaces: 2))
            .Render(report);

    private static System.Collections.Generic.List<string> Print(XReportSummary report) =>
        EscPosDump.PrintedLines(Render(report));

    /// <summary>
    /// The same shift the integration-level hand-worked example uses
    /// (<c>XReportServiceTests.FR_8_3_XReportFiguresMatchHandComputedValuesOnASeededShift</c>):
    /// 2 sales (220.00 CASH, 110.00 CARD), 1 return (110.00 CASH), one 10% tax bracket, a
    /// 1000.00 cash-in float top-up.
    /// </summary>
    private static XReportSummary Report()
    {
        var openedAt = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.FromHours(5.5));
        var generatedAt = openedAt.AddHours(3);

        return new XReportSummary(
            ShiftId: 1,
            ShiftNo: "SH-000001",
            UserId: 7,
            CashierDisplayName: "Priya",
            OpenedAt: openedAt,
            GeneratedAt: generatedAt,
            ShiftDuration: generatedAt - openedAt,
            OpeningFloat: Money.Zero,
            SalesCount: 2,
            SalesValue: Money.FromDecimal(330.00m),
            ReturnsCount: 1,
            ReturnsValue: Money.FromDecimal(110.00m),
            DiscountTotal: Money.Zero,
            SalesTaxTotal: Money.FromDecimal(30.00m),
            ReturnsTaxTotal: Money.FromDecimal(10.00m),
            TaxBreakdown:
            [
                new XReportTaxBreakdownLine(TaxRate.FromPercent(10m), Money.FromDecimal(270.00m), Money.FromDecimal(30.00m)),
            ],
            Tenders:
            [
                new XReportTenderLine(
                    TenderTypes.Cash, Money.FromDecimal(220.00m), Money.FromDecimal(110.00m), Money.FromDecimal(110.00m)),
                new XReportTenderLine(
                    TenderTypes.Card, Money.FromDecimal(110.00m), Money.Zero, Money.FromDecimal(110.00m)),
            ],
            CashMovements:
            [
                new CashMovementRecord(
                    1, 1, CashMovementDirection.In, Money.FromDecimal(1000m), "Float top-up", 7, "Priya", openedAt.AddMinutes(30)),
            ],
            ExpectedCash: new ExpectedCashSummary(
                ShiftId: 1,
                OpeningFloat: Money.Zero,
                CashSales: Money.FromDecimal(220.00m),
                CashRefunds: Money.FromDecimal(110.00m),
                CashIn: Money.FromDecimal(1000m),
                CashOut: Money.Zero,
                ExpectedCash: Money.FromDecimal(1110.00m)));
    }

    /// <summary>Index of the first occurrence of <paramref name="needle"/>, or -1.</summary>
    private static int FindSequence(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            var found = true;
            for (var i = 0; i < needle.Length; i++)
            {
                if (haystack[start + i] != needle[i])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return start;
            }
        }

        return -1;
    }
}
