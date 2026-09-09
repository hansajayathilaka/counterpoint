using System;
using Counterpoint.Ui.Input;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// The part of the scanner keystroke filter that has nothing to do with a window (SRS FR-3.2,
/// UI-01, P1-T09 "Do this" #4 - "inter-key timing under 30 ms plus configurable prefix/suffix").
/// </summary>
public sealed class ScannerKeystrokeFilterTests
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromMilliseconds(30);
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FR_3_2_ARunFasterThanTheGapBecomesAConfirmedScanOnceItReachesTheMinimumLength()
    {
        var filter = new ScannerKeystrokeFilter(minimumLength: 6, MaxGap);

        var at = Start;
        foreach (var character in "123456")
        {
            filter.Push(character, at);
            at += TimeSpan.FromMilliseconds(5);
        }

        filter.IsConfirmedScanInProgress.Should().BeTrue();
        filter.Commit().Should().Be("123456");
    }

    [Fact]
    public void FR_3_2_TypingSlowerThanTheGapIsNeverConfirmed()
    {
        var filter = new ScannerKeystrokeFilter(minimumLength: 6, MaxGap);

        var at = Start;
        foreach (var character in "123456")
        {
            filter.Push(character, at);
            at += TimeSpan.FromMilliseconds(100);
        }

        filter.IsConfirmedScanInProgress.Should().BeFalse();
        filter.Commit().Should().BeNull("every gap was slower than scanner speed - this was typing");
    }

    [Fact]
    public void FR_3_2_AShortFastBurstBelowTheMinimumLengthIsNotAScan()
    {
        var filter = new ScannerKeystrokeFilter(minimumLength: 6, MaxGap);

        var at = Start;
        foreach (var character in "123")
        {
            filter.Push(character, at);
            at += TimeSpan.FromMilliseconds(5);
        }

        filter.IsConfirmedScanInProgress.Should().BeFalse();
        filter.Commit().Should().BeNull("three characters is below the configured minimum length");
    }

    [Fact]
    public void P1_T09_ASlowKeystrokeInTheMiddleResetsTheRunSoOnlyTheFastTailCanBeConfirmed()
    {
        var filter = new ScannerKeystrokeFilter(minimumLength: 4, MaxGap);

        var at = Start;
        filter.Push('9', at); // one slow keystroke, alone
        at += TimeSpan.FromMilliseconds(200);

        // Then a fast run starts fresh from here.
        foreach (var character in "1234")
        {
            filter.Push(character, at);
            at += TimeSpan.FromMilliseconds(5);
        }

        filter.Commit().Should().Be(
            "1234", "the slow keystroke does not carry into the fast run that happens to follow it");
    }

    [Fact]
    public void CommitClearsTheBufferForTheNextScan()
    {
        var filter = new ScannerKeystrokeFilter(minimumLength: 2, MaxGap);

        var at = Start;
        filter.Push('1', at);
        filter.Push('2', at + TimeSpan.FromMilliseconds(5));
        filter.Commit().Should().Be("12");

        filter.BufferedLength.Should().Be(0);
        filter.IsConfirmedScanInProgress.Should().BeFalse();
    }

    [Fact]
    public void JustBecameConfirmedScanIsTrueExactlyOnTheConfirmingKeystroke()
    {
        var filter = new ScannerKeystrokeFilter(minimumLength: 3, MaxGap);

        var at = Start;
        filter.Push('1', at).Should().BeTrue();
        filter.JustBecameConfirmedScan.Should().BeFalse("not long enough yet");

        at += TimeSpan.FromMilliseconds(5);
        filter.Push('2', at);
        filter.JustBecameConfirmedScan.Should().BeFalse("still not long enough");

        at += TimeSpan.FromMilliseconds(5);
        filter.Push('3', at);
        filter.JustBecameConfirmedScan.Should().BeTrue("this keystroke reached the minimum length");

        at += TimeSpan.FromMilliseconds(5);
        filter.Push('4', at);
        filter.JustBecameConfirmedScan.Should().BeFalse("already confirmed by the previous keystroke");
    }
}
