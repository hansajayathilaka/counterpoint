using System;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Security;

/// <summary>
/// The single-use override token (SRS FR-1.7).
/// </summary>
/// <remarks>
/// It is built here through <see cref="OwnerOverrideService"/>'s own route in the integration
/// tests; these are the token's own rules, exercised in isolation via the internals-visible
/// constructor the assembly grants the test project.
/// </remarks>
public sealed class OverrideTokenTests
{
    private static readonly DateTimeOffset Granted =
        new(2026, 9, 7, 11, 0, 0, TimeSpan.FromHours(5.5));

    private static OverrideToken Token() => Build("DISCOUNT_ABOVE_LIMIT", Granted);

    [Fact]
    public void FR_1_7_ATokenCarriesBothTheCashierAndTheOwner()
    {
        var token = Token();

        token.RequestedByUserId.Should().Be(7, "the cashier who asked");
        token.GrantedByUserId.Should().Be(1, "the owner who allowed it");
        token.Action.Should().Be("DISCOUNT_ABOVE_LIMIT");
    }

    [Fact]
    public void FR_1_7_ATokenIsSpentExactlyOnce()
    {
        var token = Token();

        token.TryConsume("DISCOUNT_ABOVE_LIMIT", Granted).Should().BeTrue();
        token.IsConsumed.Should().BeTrue();

        token.TryConsume("DISCOUNT_ABOVE_LIMIT", Granted).Should().BeFalse(
            "one yes from the owner authorises one action, not every action after it");
    }

    [Fact]
    public void ATokenCannotBeSpentOnADifferentAction()
    {
        var token = Token();

        token.TryConsume("UNLINKED_RETURN", Granted).Should().BeFalse();
        token.IsConsumed.Should().BeFalse(
            "asking for the wrong action must not burn an override the cashier is still entitled to");

        token.TryConsume("DISCOUNT_ABOVE_LIMIT", Granted).Should().BeTrue();
    }

    [Fact]
    public void ATokenExpires()
    {
        var token = Token();

        token.ExpiresAt.Should().Be(Granted + OverrideToken.Validity);

        token.TryConsume("DISCOUNT_ABOVE_LIMIT", Granted + OverrideToken.Validity)
            .Should().BeTrue("the last instant of the window still counts");

        var stale = Build("DISCOUNT_ABOVE_LIMIT", Granted);

        stale.TryConsume("DISCOUNT_ABOVE_LIMIT", Granted + OverrideToken.Validity + TimeSpan.FromSeconds(1))
            .Should().BeFalse("a token left on an unattended screen is worthless by the time anyone reaches it");
    }

    [Fact]
    public async Task OnlyOneOfManyConcurrentCallersSpendsTheToken()
    {
        var token = Token();

        var attempts = new Task<bool>[16];
        for (var i = 0; i < attempts.Length; i++)
        {
            attempts[i] = Task.Run(() => token.TryConsume("DISCOUNT_ABOVE_LIMIT", Granted));
        }

        var results = await Task.WhenAll(attempts);

        results.Should().ContainSingle(spent => spent);
    }

    /// <summary>
    /// Mints a token the way the override service does. The constructor is internal - only
    /// <see cref="IOwnerOverrideService"/> may issue one - and this project is granted access to
    /// it by an <c>InternalsVisibleTo</c> in Counterpoint.Application, rather than the type being
    /// opened up to anything that fancies forging an override.
    /// </summary>
    private static OverrideToken Build(string action, DateTimeOffset grantedAt) =>
        new(action, requestedByUserId: 7, grantedByUserId: 1, grantedAt);
}
