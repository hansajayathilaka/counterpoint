using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-21, the shift and cash-variance history (task P3-T06 "Do this" #4, SRS FR-8.6), over 30 closed shifts
/// with deterministic over/short values, one per day from Sep 6, plus one still-open shift on Oct 6.
/// </summary>
/// <remarks>
/// <code>
/// Shift n (n = 1..30) opens on Sep 5+n at 09:00 (shift 1 is the seeded shift, float 0.00; the rest open with 1000.00)
/// and closes at 18:00 with counted = float + the variance below. Nothing else happens in a shift, so expected = float.
///   n: 1 +25.00  2 -120.00  3 +80.00  4 -45.50  5 +610.00(note)  6 -15.00  7 +200.00  8 -330.25(note)  9 +90.00  10 -60.00
///      11 +150.00  12 -75.00  13 +40.00  14 -210.00  15 +35.25   (earlier half)
///      16 +10.00  17 -5.00  18 0.00  19 +20.00  20 -12.50  21 +7.50  22 -30.00  23 0.00  24 +15.00  25 -2.25
///      26 +5.00  27 +4.00  28 -8.00  29 +6.00  30 -6.00          (later half)
/// net +378.25   over +1297.75   short -919.50   sum of absolutes 2217.25 (mean 2217.25 / 30)
/// earlier 15: absolutes sum 2086.00   later 15: absolutes sum 131.25 (mean 8.75)   running total ends at 378.25
/// </code>
/// </remarks>
public sealed class ShiftVarianceHistoryTests(ShiftVarianceHistoryTests.ThirtyShifts world)
    : IClassFixture<ShiftVarianceHistoryTests.ThirtyShifts>
{
    // Shift 1..30. Index 0 is the seeded shift (float 0.00), so its variance cannot be negative.
    private static readonly decimal[] Variances =
    [
        25.00m, -120.00m, 80.00m, -45.50m, 610.00m, -15.00m, 200.00m, -330.25m, 90.00m, -60.00m,
        150.00m, -75.00m, 40.00m, -210.00m, 35.25m,
        10.00m, -5.00m, 0.00m, 20.00m, -12.50m, 7.50m, -30.00m, 0.00m, 15.00m, -2.25m,
        5.00m, 4.00m, -8.00m, 6.00m, -6.00m,
    ];

    private static readonly string[] RunningTotals =
    [
        "25.00", "-95.00", "-15.00", "-60.50", "549.50", "534.50", "734.50", "404.25", "494.25", "434.25",
        "584.25", "509.25", "549.25", "339.25", "374.50",
        "384.50", "379.50", "379.50", "399.50", "387.00", "394.50", "364.50", "364.50", "379.50", "377.25",
        "382.25", "386.25", "378.25", "384.25", "378.25",
    ];

    private static readonly Dictionary<int, string> Notes = new()
    {
        [1] = "Float miscount",
        [4] = "Recounted twice, cash-up sheet attached",
        [7] = "Short after till drop",
    };

    private static DateOnly DateOf(int index) => SalesReportDataset.DayOne.AddDays(index);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private SaleFixture _host => world.Host;

    private IShiftVarianceHistoryQuery Query => _host.Resolve<IShiftVarianceHistoryQuery>();

    [Fact]
    public async Task RPT_21_ThirtyClosedShiftsGiveOneRowEachWithTheStoredCloseFieldsAndARunningTotal()
    {
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(new(2026, 9, 1), new(2026, 10, 31)));

        history.Rows.Should().HaveCount(30, "thirty closed shifts; the open thirty-first is not listed");

        for (var i = 0; i < 30; i++)
        {
            var row = history.Rows[i];
            var expected = i == 0 ? 0.00m : 1000.00m;

            row.ShiftNo.Should().Be("SH-" + (i + 1).ToString("000000", CultureInfo.InvariantCulture));
            row.BusinessDate.Should().Be(DateOf(i));
            row.ExpectedCash.Should().Be(M(expected), "shift {0}: nothing but its float", i + 1);
            row.CountedCash.Should().Be(M(expected + Variances[i]));
            row.Variance.Should().Be(M(Variances[i]), "counted minus expected; negative is short");
            row.CumulativeVariance.Should().Be(M(decimal.Parse(RunningTotals[i], CultureInfo.InvariantCulture)), "running total after shift {0}", i + 1);
            row.Note.Should().Be(Notes.GetValueOrDefault(i));
            row.OpenedBy.Should().NotBeNullOrWhiteSpace();
            row.ClosedBy.Should().Be(row.OpenedBy, "the owner opened and closed every shift");
            row.ClosedAt.Should().Be(new DateTimeOffset(DateOf(i).Year, DateOf(i).Month, DateOf(i).Day, 18, 0, 0, SalesReportDataset.ShopOffset));
        }

        history.Rows.Select(row => row.ExceedsNoteThreshold).Where(flag => flag).Should().HaveCount(1, "only the +610.00 shift is above the 500.00 threshold");
        history.Rows[4].ExceedsNoteThreshold.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_21_TheTotalsAreNetOverShortAndTheMeanAbsoluteVariance()
    {
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(new(2026, 9, 1), new(2026, 10, 31)));

        history.NetVariance.Should().Be(M(378.25m));
        history.TotalOver.Should().Be(M(1297.75m), "the positive variances");
        history.TotalShort.Should().Be(M(-919.50m), "the negative variances, as a negative number");
        (history.TotalOver + history.TotalShort).Should().Be(history.NetVariance);
        history.MeanAbsoluteVariance.Should().Be(M(2217.25m / 30m), "the sum of the absolutes over thirty shifts");
        history.NoteThreshold.Should().Be(M(500.00m), "the shipped default");
        history.ShiftsOverThreshold.Should().Be(1);

        // Independent: the raw close fields on the shift table.
        (await _host.CountAsync("SELECT SUM(variance) FROM shift WHERE status = 'CLOSED';")).Should().Be(3_782_500);
        (await _host.CountAsync("SELECT COUNT(*) FROM shift WHERE status = 'CLOSED';")).Should().Be(30);
    }

    [Fact]
    public async Task RPT_21_TheLaterHalfSwingingLessThanTheEarlierHalfReadsAsImproving()
    {
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(new(2026, 9, 1), new(2026, 10, 31)));

        history.Trend.Should().Be(VarianceTrend.Improving);
        history.EarlierHalfMeanAbsolute.Should().Be(M(2086.00m / 15m), "shifts 1-15");
        history.LaterHalfMeanAbsolute.Should().Be(M(8.75m), "shifts 16-30");
    }

    [Theory]
    [InlineData(0, 3, VarianceTrend.Improving, 72.50, 62.75)]
    [InlineData(0, 4, VarianceTrend.Worsening, 72.50, 327.75)]
    [InlineData(15, 18, VarianceTrend.Worsening, 7.50, 10.00)]
    [InlineData(26, 29, VarianceTrend.Steady, 6.00, 6.00)]
    public async Task RPT_21_TheTrendComparesTheLaterHalfWithTheEarlierHalfOfTheShiftsInTheRange(
        int firstIndex, int lastIndex, VarianceTrend expected, double earlier, double later)
    {
        // Four shifts: two against two. Five shifts: the middle one is left out.
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(DateOf(firstIndex), DateOf(lastIndex)));

        history.Rows.Should().HaveCount(lastIndex - firstIndex + 1);
        history.Trend.Should().Be(expected);
        history.EarlierHalfMeanAbsolute.Should().Be(M((decimal)earlier));
        history.LaterHalfMeanAbsolute.Should().Be(M((decimal)later));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    public async Task RPT_21_FewerThanFourClosedShiftsClaimNoTrend(int firstIndex, int lastIndex)
    {
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(DateOf(firstIndex), DateOf(lastIndex)));

        history.Rows.Should().HaveCount(lastIndex - firstIndex + 1);
        history.Trend.Should().Be(VarianceTrend.NotEnoughData);
        history.EarlierHalfMeanAbsolute.Should().Be(Money.Zero);
        history.LaterHalfMeanAbsolute.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task RPT_21_TheRunningTotalIsOverTheShiftsInTheRangeNotTheWholeHistory()
    {
        // Shifts 5-9 (Sep 10 - 14): +610.00, -15.00, +200.00, -330.25, +90.00.
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(DateOf(4), DateOf(8)));

        history.Rows.Select(row => row.CumulativeVariance).Should().Equal(
            M(610.00m), M(595.00m), M(795.00m), M(464.75m), M(554.75m));
        history.NetVariance.Should().Be(M(554.75m));
        history.TotalOver.Should().Be(M(900.00m));
        history.TotalShort.Should().Be(M(-345.25m));
        history.MeanAbsoluteVariance.Should().Be(M(1245.25m / 5m), "610 + 15 + 200 + 330.25 + 90 = 1245.25 over five shifts");
    }

    [Fact]
    public async Task RPT_21_TheNoteThresholdComesFromSettingsAndAVarianceEqualToItIsNotOverIt()
    {
        var settings = _host.Resolve<ISettings>();
        var original = (await settings.LoadAsync()).Policy.ShiftCloseVarianceNoteThreshold;

        try
        {
            await settings.UpdateAsync(current => current with
            {
                Policy = current.Policy with { ShiftCloseVarianceNoteThreshold = M(120.00m) },
            });

            var history = await Query.GetHistoryAsync(ReportDateRange.Custom(new(2026, 9, 1), new(2026, 10, 31)));

            history.NoteThreshold.Should().Be(M(120.00m));
            history.ShiftsOverThreshold.Should().Be(5, "+610.00, +200.00, -330.25, +150.00 and -210.00; the -120.00 shift is equal to it, not over");
            history.Rows.Where(row => row.ExceedsNoteThreshold).Select(row => row.Variance).Should().BeEquivalentTo(
                [M(610.00m), M(200.00m), M(-330.25m), M(150.00m), M(-210.00m)]);
            history.Rows[1].Variance.Should().Be(M(-120.00m));
            history.Rows[1].ExceedsNoteThreshold.Should().BeFalse();
        }
        finally
        {
            await settings.UpdateAsync(current => current with
            {
                Policy = current.Policy with { ShiftCloseVarianceNoteThreshold = original },
            });
        }
    }

    [Fact]
    public async Task RPT_21_ARangeWithNoClosedShiftIsEmptyWithZeroTotalsAndNoTrend()
    {
        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)));

        history.Rows.Should().BeEmpty();
        history.NetVariance.Should().Be(Money.Zero);
        history.TotalOver.Should().Be(Money.Zero);
        history.TotalShort.Should().Be(Money.Zero);
        history.MeanAbsoluteVariance.Should().Be(Money.Zero, "no division by zero");
        history.ShiftsOverThreshold.Should().Be(0);
        history.Trend.Should().Be(VarianceTrend.NotEnoughData);
        history.NoteThreshold.Should().Be(M(500.00m));
    }

    [Fact]
    public async Task RPT_21_AStillOpenShiftHasNoVarianceYetAndIsNotListed()
    {
        var october = DateOf(30);

        var history = await Query.GetHistoryAsync(ReportDateRange.Custom(october, october));

        history.Rows.Should().BeEmpty();
        (await _host.CountAsync("SELECT COUNT(*) FROM shift WHERE status = 'OPEN' AND business_date = '2026-10-06';")).Should().Be(1, "the open shift exists");
    }

    /// <summary>The thirty-one shifts above, built once over one real encrypted database and shared by the class's tests.</summary>
    public sealed class ThirtyShifts : IAsyncLifetime
    {
        internal SaleFixture Host { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
            await SalesReportDataset.DisableBackupOnShiftCloseAsync(Host);

            var session = Host.Resolve<ISession>();
            var owner = session.CurrentUser!;

            for (var index = 0; index < Variances.Length; index++)
            {
                var date = DateOf(index);
                var openingFloat = index == 0 ? Money.Zero : M(1000.00m);

                if (index > 0)
                {
                    await Host.Resolve<IOpenShift>().OpenAsync(
                        new OpenShiftCommand(owner.Id, openingFloat, new DateTimeOffset(date.Year, date.Month, date.Day, 9, 0, 0, SalesReportDataset.ShopOffset)));
                }

                await Host.Resolve<ICloseShift>().CloseAsync(new CloseShiftCommand(
                    session.ShiftId!.Value,
                    owner.Id,
                    openingFloat + M(Variances[index]),
                    new DateTimeOffset(date.Year, date.Month, date.Day, 18, 0, 0, SalesReportDataset.ShopOffset),
                    Note: Notes.GetValueOrDefault(index)));
            }

            // A thirty-first shift that is still open: it has no variance yet and must not be listed.
            var october = DateOf(Variances.Length);
            await Host.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(
                owner.Id, M(1000.00m), new DateTimeOffset(october.Year, october.Month, october.Day, 9, 0, 0, SalesReportDataset.ShopOffset)));
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();
    }
}
