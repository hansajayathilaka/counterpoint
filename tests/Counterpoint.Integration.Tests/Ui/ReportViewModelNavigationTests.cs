using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Ui.ViewModels.Reports;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// <see cref="BillDrillDownViewModel"/> navigation (task P3-T05 "Do this" #5): summary -> bill list
/// -> bill -> Back. Driven against a scripted <see cref="ISalesBillQuery"/> so every branch -
/// truncation, an empty list, a missing bill, a refusal - can be reached; the same navigation over
/// real data is <see cref="ReportDrillDownEndToEndTests"/>.
/// </summary>
public sealed class ReportViewModelNavigationTests
{
    private static readonly ReportDateRange Day = ReportDateRange.Custom(new(2026, 9, 6), new(2026, 9, 6));

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    [Fact]
    public async Task RPT_01_SummaryToListToBillAndBackReturnsThroughEachLevel()
    {
        var query = new ScriptedBillQuery();
        var drill = new BillDrillDownViewModel(query);

        drill.Level.Should().Be(DrillLevel.None);
        drill.IsActive.Should().BeFalse();

        await drill.OpenBillListAsync(new BillListFilter(Day), "Bills on 2026-09-06");

        drill.Level.Should().Be(DrillLevel.BillList);
        drill.IsBillList.Should().BeTrue();
        drill.Title.Should().Be("Bills on 2026-09-06");
        drill.Bills.Select(row => row.BillNo).Should().Equal(["INV-1", "INV-2"]);
        drill.Note.Should().Be("2 bills.");
        query.ListFilters.Should().ContainSingle().Which.Range.Should().Be(Day);

        await drill.OpenBillCommand.ExecuteAsync(drill.Bills[1]);

        query.Opened.Should().Equal([2L], "the bill opened is the row's own bill, not the first in the list");
        drill.Level.Should().Be(DrillLevel.Bill);
        drill.IsBill.Should().BeTrue();
        drill.Title.Should().Be("Bill INV-2");
        drill.Bill!.BillNo.Should().Be("INV-2");
        drill.Bill.Lines.Should().ContainSingle().Which.Description.Should().Be("Line of INV-2");
        drill.Bill.Payments.Should().ContainSingle().Which.TenderType.Should().Be("CASH");

        drill.BackCommand.Execute(null);

        drill.Level.Should().Be(DrillLevel.BillList, "Back from a bill reached through the list returns to the list");
        drill.Bill.Should().BeNull();
        drill.Bills.Should().HaveCount(2, "the list is still there, not re-queried");
        query.ListFilters.Should().HaveCount(1);

        drill.BackCommand.Execute(null);

        drill.Level.Should().Be(DrillLevel.None, "Back from the list returns to the summary");
        drill.IsActive.Should().BeFalse();
        drill.Bills.Should().BeEmpty();
    }

    [Fact]
    public async Task RPT_01_ABillOpenedDirectlyGoesBackStraightToTheSummary()
    {
        var drill = new BillDrillDownViewModel(new ScriptedBillQuery());

        await drill.OpenBillByIdAsync(5, fromList: false);

        drill.Level.Should().Be(DrillLevel.Bill);

        drill.BackCommand.Execute(null);

        drill.Level.Should().Be(DrillLevel.None, "there was no list to go back to");
    }

    [Fact]
    public async Task RPT_01_CloseAtAnyLevelReturnsToTheSummaryAndClearsEverything()
    {
        var drill = new BillDrillDownViewModel(new ScriptedBillQuery());
        await drill.OpenBillListAsync(new BillListFilter(Day), "Bills");
        await drill.OpenBillCommand.ExecuteAsync(drill.Bills[0]);

        drill.CloseCommand.Execute(null);

        drill.Level.Should().Be(DrillLevel.None);
        drill.Bill.Should().BeNull();
        drill.Bills.Should().BeEmpty();
        drill.Note.Should().BeEmpty();
    }

    [Fact]
    public async Task RPT_01_ATruncatedListSaysSoAndAnEmptyListSaysThatInstead()
    {
        var query = new ScriptedBillQuery { Truncated = true };
        var drill = new BillDrillDownViewModel(query);

        await drill.OpenBillListAsync(new BillListFilter(Day, MaxRows: 2), "Bills");

        drill.Note.Should().Contain("first 2 bills").And.Contain("narrow the date range");

        query.Truncated = false;
        query.Bills = [];
        await drill.OpenBillListAsync(new BillListFilter(Day), "Bills");

        drill.Note.Should().Be("No bills.");
        drill.Level.Should().Be(DrillLevel.BillList, "an empty list is still a list, so Back works from it");
    }

    [Fact]
    public async Task RPT_01_ABillThatCannotBeFoundSaysSoAndStaysWhereItWas()
    {
        var drill = new BillDrillDownViewModel(new ScriptedBillQuery());
        await drill.OpenBillListAsync(new BillListFilter(Day), "Bills");

        await drill.OpenBillByIdAsync(404, fromList: true);

        drill.Note.Should().Be("That bill could not be found.");
        drill.Level.Should().Be(DrillLevel.BillList);
        drill.Bill.Should().BeNull();
    }

    [Fact]
    public async Task AC_17_ARefusalFromTheQueryIsShownNotThrownThroughTheScreen()
    {
        var drill = new BillDrillDownViewModel(new ScriptedBillQuery { Refuse = true });

        await drill.OpenBillListAsync(new BillListFilter(Day), "Bills");

        drill.Note.Should().Be("Refused for the test.");
        drill.Level.Should().Be(DrillLevel.None, "nothing opened, so nothing to go back from");
        drill.Busy.Should().BeFalse("a refusal must never leave the screen stuck busy");
    }

    private sealed class ScriptedBillQuery : ISalesBillQuery
    {
        internal bool Truncated { get; set; }

        internal bool Refuse { get; set; }

        internal IReadOnlyList<SalesBillRow> Bills { get; set; } = [Row(1, "INV-1"), Row(2, "INV-2")];

        internal List<BillListFilter> ListFilters { get; } = [];

        internal List<long> Opened { get; } = [];

        public Task<SalesBillList> GetBillsAsync(BillListFilter filter, CancellationToken cancellationToken = default)
        {
            if (Refuse)
            {
                throw new NotAuthorisedException("Refused for the test.");
            }

            ListFilters.Add(filter);

            return Task.FromResult(new SalesBillList(Bills, Truncated));
        }

        public Task<SalesBillDetail?> GetBillAsync(long saleId, CancellationToken cancellationToken = default)
        {
            Opened.Add(saleId);

            return Task.FromResult<SalesBillDetail?>(saleId == 404 ? null : Detail(saleId));
        }

        private static SalesBillRow Row(long id, string billNo) => new(
            id, billNo, new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(5.5)), new DateOnly(2026, 9, 6),
            "Walk-in", "Cashier", M(100m), Money.Zero, M(10m), M(100m), M(110m), Money.Zero);

        private static SalesBillDetail Detail(long id) => new(
            id, "INV-" + id, new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(5.5)), new DateOnly(2026, 9, 6),
            "COMPLETED", "Cashier", "Walk-in", M(100m), Money.Zero, Money.Zero, M(10m), Money.Zero, M(110m), null,
            [new SalesBillLine(1, "Line of INV-" + id, Quantity.FromDecimal(1m, 1), "pc", M(100m), Money.Zero, M(10m), M(100m), Quantity.Zero(0))],
            [new SalesBillPayment("CASH", M(110m))],
            []);
    }
}
