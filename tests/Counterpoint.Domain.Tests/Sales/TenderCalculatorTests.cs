using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.Sales;
using Counterpoint.Domain.Tests.Support;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Sales;

/// <summary>
/// Splitting a bill total across tenders and computing change (P1-T10, SRS FR-3.24-FR-3.26).
/// </summary>
public sealed class TenderCalculatorTests
{
    [Fact]
    public void FR_3_24_AnExactSingleCashTenderAppliesInFullWithNoChange()
    {
        var plan = TenderCalculator.Calculate(
            Money.FromDecimal(25.00m),
            [new TenderLine(TenderCalculator.Cash, Money.FromDecimal(25.00m))]);

        plan.Applied.Should().ContainSingle();
        plan.Applied[0].Amount.Should().Be(Money.FromDecimal(25.00m));
        plan.Change.Should().Be(Money.Zero);
    }

    [Fact]
    public void FR_3_26_CashOfferedOverTheTotalIsCappedAtTheTotalAndTheExcessBecomesChange()
    {
        var plan = TenderCalculator.Calculate(
            Money.FromDecimal(25.00m),
            [new TenderLine(TenderCalculator.Cash, Money.FromDecimal(30.00m))]);

        plan.Applied.Should().ContainSingle();
        plan.Applied[0].Amount.Should().Be(
            Money.FromDecimal(25.00m),
            "a payment row is what the bill was actually paid, never what cash ran over by");
        plan.Change.Should().Be(Money.FromDecimal(5.00m));
    }

    [Fact]
    public void FR_3_25_ACardTenderOfferedForMoreThanTheBillStillOwesIsRefusedOutright()
    {
        var act = () => TenderCalculator.Calculate(
            Money.FromDecimal(25.00m),
            [new TenderLine("CARD", Money.FromDecimal(30.00m))]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot take more than*")
            .Which.Message.Should().Contain("Change is cash only");
    }

    [Theory]
    [InlineData("CARD")]
    [InlineData("BANK_TRANSFER")]
    [InlineData("CHEQUE")]
    public void FR_3_25_EveryNonCashTenderTypeIsCappedAtWhatIsOwedNeverAllowedToOverpay(string tenderType)
    {
        var act = () => TenderCalculator.Calculate(
            Money.FromDecimal(10.00m),
            [new TenderLine(tenderType, Money.FromDecimal(10.01m))]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Change is cash only*");
    }

    [Fact]
    public void FR_3_24_ASplitTenderAppliesEachTenderInTheOrderOfferedAndSumsToTheTotal()
    {
        var plan = TenderCalculator.Calculate(
            Money.FromDecimal(100.00m),
            [
                new TenderLine("CARD", Money.FromDecimal(40.00m), "SLIP-001"),
                new TenderLine(TenderCalculator.Cash, Money.FromDecimal(60.00m)),
            ]);

        plan.Applied.Should().HaveCount(2);
        plan.Applied[0].TenderType.Should().Be("CARD");
        plan.Applied[0].Amount.Should().Be(Money.FromDecimal(40.00m));
        plan.Applied[0].Reference.Should().Be("SLIP-001");
        plan.Applied[1].TenderType.Should().Be(TenderCalculator.Cash);
        plan.Applied[1].Amount.Should().Be(Money.FromDecimal(60.00m));
        plan.Change.Should().Be(Money.Zero);

        var sumApplied = plan.Applied.Aggregate(Money.Zero, (running, tender) => running + tender.Amount);
        sumApplied.Should().Be(Money.FromDecimal(100.00m));
    }

    [Fact]
    public void FR_3_24_CashOfferedLastInASplitCanStillRunOverTheRemainingBalanceIntoChange()
    {
        // 100 total, 70 already covered by card, then 40 cash offered against the 30 still
        // owed - the excess 10 is change, exactly as if cash were the only tender.
        var plan = TenderCalculator.Calculate(
            Money.FromDecimal(100.00m),
            [
                new TenderLine("CARD", Money.FromDecimal(70.00m)),
                new TenderLine(TenderCalculator.Cash, Money.FromDecimal(40.00m)),
            ]);

        plan.Applied[1].Amount.Should().Be(Money.FromDecimal(30.00m));
        plan.Change.Should().Be(Money.FromDecimal(10.00m));
    }

    [Fact]
    public void UnderTenderIsRejectedWithAClearMessageNamingTheShortfall()
    {
        var act = () => TenderCalculator.Calculate(
            Money.FromDecimal(25.00m),
            [new TenderLine(TenderCalculator.Cash, Money.FromDecimal(20.00m))]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must match exactly*");
    }

    [Fact]
    public void OverTenderSplitAcrossMultipleTendersIsRejectedEvenWhenCashAloneWouldHaveCovered()
    {
        // Cash alone already reaches the total; the second, non-cash tender then has nothing
        // left to be applied against and is refused rather than silently ignored or banked as a
        // second change.
        var act = () => TenderCalculator.Calculate(
            Money.FromDecimal(25.00m),
            [
                new TenderLine(TenderCalculator.Cash, Money.FromDecimal(25.00m)),
                new TenderLine("CARD", Money.FromDecimal(5.00m)),
            ]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Change is cash only*");
    }

    [Fact]
    public void NoTenderOfferedIsRejected()
    {
        var act = () => TenderCalculator.Calculate(Money.FromDecimal(25.00m), []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*must be tendered*");
    }

    [Fact]
    public void ATenderOfZeroOrLessIsRejected()
    {
        var act = () => TenderCalculator.Calculate(
            Money.FromDecimal(25.00m),
            [new TenderLine(TenderCalculator.Cash, Money.Zero)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than zero*");
    }

    /// <summary>
    /// Generative: 2 000 random bills, each split across one to four random tenders that are
    /// engineered to sum to the total exactly (the last tender is always cash, sized to make up
    /// whatever remains, so the split itself never needs to be exact by luck). Whatever the split,
    /// the plan must always apply exactly the total and never negative, exactly like
    /// <c>StockLedgerMathTests</c> and <c>DiscountAllocatorTests</c> prove their own identities by
    /// construction rather than by example (P1-T10).
    /// </summary>
    [Fact]
    public void GenerativeAppliedTendersAlwaysSumToTheTotalAndChangeIsAlwaysNonNegative()
    {
        var sample = new DeterministicSample(seed: 20261010);
        string[] nonCashTypes = ["CARD", "BANK_TRANSFER", "CHEQUE"];

        for (var trial = 0; trial < 2_000; trial++)
        {
            var total = Money.FromDecimal(sample.NextStorableDecimal(0.01m, 5_000.00m));
            var nonCashCount = sample.NextInt(0, 4);

            var tenders = new List<TenderLine>();
            var remaining = total;

            for (var i = 0; i < nonCashCount && remaining.IsPositive; i++)
            {
                // Never more than what remains: exercising the split without tripping the
                // over-tender refusal, which is proven separately above.
                var amount = Money.FromDecimal(sample.NextStorableDecimal(0.01m, remaining.Amount + 0.01m));

                if (amount > remaining || amount.IsZero)
                {
                    continue;
                }

                var tenderType = nonCashTypes[sample.NextInt(0, nonCashTypes.Length)];
                tenders.Add(new TenderLine(tenderType, amount));
                remaining -= amount;
            }

            // Cash always closes the bill, and is free to run over: an extra random amount of
            // change on top of covering the rest, exactly like a cashier rounding up to a note.
            var change = Money.FromDecimal(sample.NextStorableDecimal(0.00m, 50.00m));
            tenders.Add(new TenderLine(TenderCalculator.Cash, remaining + change));

            var plan = TenderCalculator.Calculate(total, tenders);

            var sumApplied = plan.Applied.Aggregate(Money.Zero, (running, tender) => running + tender.Amount);
            sumApplied.Should().Be(
                total,
                "seed {0}, trial {1}: applied tenders must sum to exactly the total", sample.Seed, trial);

            plan.Change.Should().Be(
                change,
                "seed {0}, trial {1}: the only overpayment offered was cash, so change is exactly that",
                sample.Seed, trial);

            plan.Applied.Should().OnlyContain(
                tender => !tender.Amount.IsNegative,
                "seed {0}, trial {1}: no applied tender is ever negative", sample.Seed, trial);
        }
    }
}
