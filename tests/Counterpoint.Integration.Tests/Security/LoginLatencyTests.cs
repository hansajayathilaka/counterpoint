using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// Runs the login-latency measurement on its own, with the rest of the suite held back.
/// </summary>
/// <remarks>
/// Argon2id at four lanes is deliberately CPU-bound. Measured while a dozen other test classes
/// are competing for the same four cores, the number that comes out is a fact about the
/// scheduler and not about the sign-in - so this collection is taken out of the parallel run.
/// </remarks>
[CollectionDefinition(LoginLatencyTests.CollectionName, DisableParallelization = true)]
public sealed class LoginLatencyMarker
{
}

/// <summary>
/// What a sign-in costs with the work factors the shop will actually run
/// (P1-T02 "login completes well under 500 ms on the dev machine").
/// </summary>
/// <remarks>
/// <para>
/// <b>An indicative figure, and nothing more.</b> CLAUDE.md is explicit that absolute NFR
/// budgets are measured on the shop terminal in <c>HW-T07</c> and that development-machine
/// numbers do not go in <c>docs/perf-baseline.md</c>. This exists to catch the specific mistake
/// of shipping work factors that make the till feel broken - which is a real risk, because the
/// Argon2 implementation here is managed code and its cost is not obvious from the numbers.
/// </para>
/// <para>
/// The best of three runs, on purpose. The question is what the operation costs, not what a
/// shared build agent was doing at the time, and a floor is the honest way to ask that.
/// </para>
/// </remarks>
[Collection(CollectionName)]
public sealed class LoginLatencyTests
{
    internal const string CollectionName = "login-latency";

    [Fact]
    public async Task LoginCompletesWellUnderHalfASecondWithTheShippedWorkFactors()
    {
        // Argon2Parameters.Default, not the cheap test ones: the number is meaningless otherwise.
        await using var fixture = await SaleFixture.CreateAsync(hashing: Argon2Parameters.Default);
        await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync("owner", "till2026");

        var authentication = fixture.Resolve<IAuthenticationService>();

        // Warmed, so the figure is the sign-in and not the first EF query of the process.
        await authentication.LogInAsync("owner", "wrong");

        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var started = Stopwatch.StartNew();
            var result = await authentication.LogInAsync("owner", "till2026");
            started.Stop();

            result.Succeeded.Should().BeTrue();

            if (started.Elapsed < best)
            {
                best = started.Elapsed;
            }
        }

        best.Should().BeLessThan(
            TimeSpan.FromMilliseconds(500),
            "a cashier waits for this every morning; the terminal figure and the 200 ms target are HW-T07's");
    }
}
