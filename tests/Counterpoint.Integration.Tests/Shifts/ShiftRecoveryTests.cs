using System;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Shifts;

/// <summary>
/// FR-8.7: "the system must ... recover the open shift cleanly on restart."
/// </summary>
/// <remarks>
/// This is a single-process desktop application (CLAUDE.md), so there is no separate process to
/// kill and restart in a test. What <c>Counterpoint.App.Program.Main</c> actually does on every
/// start is construct a brand-new <see cref="Session"/> - empty, with nothing in memory - and ask
/// <see cref="IAuthenticationService.LogInAsync"/> to fill it in, which reads the one open shift
/// through <see cref="ITillSessionProvider"/> (<c>SqliteTillSessionProvider</c>, a live SQL query,
/// not a cache). These tests build exactly that: a second, from-scratch <see cref="Session"/> and
/// <see cref="AuthenticationService"/> pair over the same database file the first one wrote to,
/// with nothing carried over between them but the file - which is what proves the recovered
/// answer came from a database read and not from anything remembered in the first session's
/// memory.
/// </remarks>
public sealed class ShiftRecoveryTests
{
    [Fact]
    public async Task FR_8_7_RestartingTheProcessRecoversTheOpenShiftFromTheDatabaseNotFromMemory()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        var opened = await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(
                fixture.Resolve<ISession>().CurrentUser!.Id,
                Money.FromDecimal(7500m),
                fixture.Resolve<TimeProvider>().GetLocalNow()));

        // A brand-new Session and AuthenticationService, with nothing carried over except the
        // database file both share - Program.Main's own composition root builds exactly these
        // fresh on every start (Counterpoint.App.DependencyInjection.AddCounterpointSecurity).
        var freshSession = new Session();
        var freshAuth = new AuthenticationService(
            fixture.Resolve<IUserStore>(),
            fixture.Resolve<IPasswordHasher>(),
            fixture.Resolve<IAuditTrail>(),
            fixture.Resolve<IUnitOfWork>(),
            fixture.Resolve<ITillSessionProvider>(),
            freshSession,
            fixture.Resolve<TimeProvider>());

        freshSession.ShiftId.Should().BeNull("nothing has signed in on this fresh session yet");

        var result = await freshAuth.LogInAsync(SaleFixture.SeededOwnerUsername, SaleFixture.SeededOwnerPassword);

        result.Succeeded.Should().BeTrue(result.Message);
        freshSession.ShiftId.Should().Be(
            opened.ShiftId,
            "the one OPEN row in the database is what a restarted process recovers, not any prior in-memory session");

        // "Correct totals": the opening float this second session's shift is trading against is
        // exactly what was set before the restart - read straight off the row, not carried in
        // memory either.
        (await fixture.ScalarAsync("SELECT opening_float FROM shift WHERE id = " + opened.ShiftId + ";"))
            .Should().Be("75000000");
    }

    [Fact]
    public async Task FR_8_7_ATillSessionProviderQueriedTwiceAfterAShiftOpensAgreesWithoutAnyCachedState()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        var provider = fixture.Resolve<ITillSessionProvider>();

        (await provider.GetCurrentAsync()).Should().BeNull("the shift was just closed and none is open yet");

        var opened = await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(
                fixture.Resolve<ISession>().CurrentUser!.Id,
                Money.FromDecimal(1000m),
                fixture.Resolve<TimeProvider>().GetLocalNow()));

        var current = await provider.GetCurrentAsync();

        current.Should().NotBeNull();
        current!.ShiftId.Should().Be(opened.ShiftId);
    }
}
