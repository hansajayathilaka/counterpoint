using System;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Shifts;
using Counterpoint.Device.Tests.Support;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;
using VerifyXunit;

namespace Counterpoint.Device.Tests.Printing;

/// <summary>
/// The fixed-layout bridge from a Z report to a byte stream (SRS FR-7.1, FR-8.4, task P3-T03
/// "Do this" #2).
/// </summary>
public sealed class EscPosZReportRendererTests
{
    [Fact]
    public void FR_8_4_TheShiftHeaderCloserAndFiguresReachThePaper()
    {
        var lines = Print(Report());

        lines.Should().Contain(line => line.Contains("Z REPORT", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("SH-000001", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Priya", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Closed by", StringComparison.Ordinal)
            && line.Contains("Anil", StringComparison.Ordinal), "shift.closed_by can differ from who opened it (SRS FR-8.7)");

        lines.Should().Contain(line => line.TrimEnd().EndsWith("220.00", StringComparison.Ordinal));
    }

    [Fact]
    public void FR_8_4_TheCountedCashAndVarianceReachThePaperSignedCorrectly()
    {
        var lines = Print(Report());

        lines.Should().Contain(line => line.Contains("Counted cash", StringComparison.Ordinal)
            && line.TrimEnd().EndsWith("200.00", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("Variance", StringComparison.Ordinal)
            && line.TrimEnd().EndsWith("-20.00", StringComparison.Ordinal),
            "a short variance must read with a minus, never as a bare magnitude");
    }

    [Fact]
    public void AnOverVarianceReadsWithAPlusSign()
    {
        var over = Report() with
        {
            CountedCash = Money.FromDecimal(250.00m),
            Variance = Money.FromDecimal(30.00m),
        };

        var lines = Print(over);

        lines.Should().Contain(line => line.Contains("Variance", StringComparison.Ordinal)
            && line.TrimEnd().EndsWith("+30.00", StringComparison.Ordinal));
    }

    [Fact]
    public void ANoteIsPrintedWhenPresentAndOmittedWhenNotGiven()
    {
        var withNote = Report() with { Note = "Till was short - counted twice" };
        var withoutNote = Report() with { Note = null };

        Print(withNote).Should().Contain(
            line => line.Contains("Note: Till was short - counted twice", StringComparison.Ordinal));
        Print(withoutNote).Should().NotContain(line => line.Contains("Note:", StringComparison.Ordinal));
    }

    [Fact]
    public void ItStatesThatTheShiftIsLockedAndAcceptsNoFurtherTransactions()
    {
        var lines = Print(Report());

        lines.Should().Contain(line => line.Contains("Shift closed - locked", StringComparison.Ordinal));
    }

    [Fact]
    public void ItNeverKicksTheDrawer()
    {
        // ESC p 0 25 250 - the same drawer-kick sequence the sale receipt renderer emits for a
        // cash tender. A Z report totals what already happened; it is not itself a cash event.
        var kick = new byte[] { 0x1B, 0x70, 0x00, 25, 250 };

        FindSequence(Render(Report()), kick).Should().Be(-1, "a Z report never opens the drawer");
    }

    [Fact]
    public void TheReceiptEndsWithACut()
    {
        // GS V 1 - partial cut, PrinterCapabilities' default.
        FindSequence(Render(Report()), [0x1D, 0x56, 1]).Should().BeGreaterThan(-1);
    }

    [Fact]
    public Task Srs_10_1_TheZReportRendersToTheCommittedByteStream()
    {
        var bytes = Render(Report());

        return Verifier.Verify(EscPosDump.Describe(bytes)).UseDirectory("Snapshots");
    }

    private static byte[] Render(ZReportSummary report) =>
        new EscPosZReportRenderer(new EscPosRenderer(), new HalfAwayFromZeroRounding(decimalPlaces: 2))
            .Render(report);

    private static System.Collections.Generic.List<string> Print(ZReportSummary report) =>
        EscPosDump.PrintedLines(Render(report));

    /// <summary>
    /// The same shift <c>CloseShiftHandlerTests.AC_11_ZReportVarianceIsComputedCorrectlyAgainstADeliberatelyMiscountedDrawerAndTheShiftLocks</c>
    /// closes: 2 pieces sold CASH for 220.00 total (10% tax), counted 200.00 against an expected
    /// 220.00 - a 20.00 short variance, no note needed at that magnitude.
    /// </summary>
    private static ZReportSummary Report()
    {
        var openedAt = new DateTimeOffset(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));
        var closedAt = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(5.5));

        return new ZReportSummary(
            ShiftId: 1,
            ShiftNo: "SH-000001",
            UserId: 7,
            CashierDisplayName: "Priya",
            OpenedAt: openedAt,
            ClosedAt: closedAt,
            ShiftDuration: closedAt - openedAt,
            OpeningFloat: Money.Zero,
            SalesCount: 1,
            SalesValue: Money.FromDecimal(220.00m),
            ReturnsCount: 0,
            ReturnsValue: Money.Zero,
            DiscountTotal: Money.Zero,
            SalesTaxTotal: Money.FromDecimal(20.00m),
            ReturnsTaxTotal: Money.Zero,
            TaxBreakdown:
            [
                new XReportTaxBreakdownLine(TaxRate.FromPercent(10m), Money.FromDecimal(200.00m), Money.FromDecimal(20.00m)),
            ],
            Tenders:
            [
                new XReportTenderLine(
                    TenderTypes.Cash, Money.FromDecimal(220.00m), Money.Zero, Money.FromDecimal(220.00m)),
            ],
            CashMovements: [],
            ExpectedCash: Money.FromDecimal(220.00m),
            CountedCash: Money.FromDecimal(200.00m),
            Variance: Money.FromDecimal(-20.00m),
            ClosedByUserId: 9,
            ClosedByDisplayName: "Anil",
            Note: null);
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
