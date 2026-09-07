using System;
using Counterpoint.Application.Security;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Security;

/// <summary>
/// The exponential backoff behind NFR-S9. The formula it asserts is the one documented on
/// <see cref="AccountLockout"/>, which is where the reasoning lives.
/// </summary>
public sealed class AccountLockoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void NFR_S9_FewerThanFiveFailuresDoNotLockTheAccount(int failures)
    {
        AccountLockout.DurationFor(failures).Should().BeNull();
    }

    [Fact]
    public void NFR_S9_TheFifthConsecutiveFailureLocksTheAccount()
    {
        AccountLockout.MaxFailedAttempts.Should().Be(5);

        AccountLockout.DurationFor(5).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(8, 240)]
    [InlineData(9, 480)]
    public void NFR_S9_EachFurtherFailureDoublesTheLock(int failures, int seconds)
    {
        AccountLockout.DurationFor(failures).Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void NFR_S9_TheBackoffIsCappedSoTheOnlyTillIsNeverLockedOutForGood(int failures)
    {
        // A single-cashier shop has no second terminal and no administrator down the corridor.
        // Past the ceiling the lock has stopped being a security control and started being an
        // outage - see AccountLockout for the full argument.
        AccountLockout.DurationFor(failures).Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void TheBackoffNeverGoesNegativeOnAnAbsurdFailureCount()
    {
        // A shift-based doubling that overflowed would hand back a negative lockout: an account
        // that unlocks itself the more it is attacked.
        for (var failures = 5; failures < 200; failures++)
        {
            AccountLockout.DurationFor(failures).Should().BeGreaterThan(TimeSpan.Zero);
        }
    }

    [Fact]
    public void ANegativeFailureCountIsAProgrammingErrorAndSaysSo()
    {
        var duration = () => AccountLockout.DurationFor(-1);

        duration.Should().Throw<ArgumentOutOfRangeException>();
    }
}
