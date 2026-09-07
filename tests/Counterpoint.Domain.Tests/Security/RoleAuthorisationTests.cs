using System;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Security;

/// <summary>
/// The mechanics of the role decorator (SRS NFR-S2). AC-17 itself is proved against the real
/// user-administration service in <c>Counterpoint.Acceptance.Tests</c>.
/// </summary>
public sealed class RoleAuthorisationTests
{
    [Fact]
    public void AMethodWithNoRequirementIsNotGuarded()
    {
        var inner = new Spy();
        var guarded = RoleAuthorisation.Decorate<IMixedService>(inner, new FakeSession(null));

        guarded.Anyone();

        inner.Calls.Should().Be(1);
    }

    [Fact]
    public void NFR_S2_AMethodMarkedOwnerOnlyRefusesACashier()
    {
        var inner = new Spy();
        var guarded = RoleAuthorisation.Decorate<IMixedService>(inner, Cashier());

        var call = guarded.OwnerOnly;

        call.Should().Throw<NotAuthorisedException>();
        inner.Calls.Should().Be(0, "the guarded service must not run at all");
    }

    [Fact]
    public void NFR_S2_AMethodMarkedOwnerOnlyAllowsAnOwner()
    {
        var inner = new Spy();
        var guarded = RoleAuthorisation.Decorate<IMixedService>(inner, Owner());

        guarded.OwnerOnly();

        inner.Calls.Should().Be(1);
    }

    [Fact]
    public void NFR_S2_NobodySignedInIsNotSomebodyWhoMayProceed()
    {
        var inner = new Spy();
        var guarded = RoleAuthorisation.Decorate<IMixedService>(inner, new FakeSession(null));

        var call = guarded.OwnerOnly;

        call.Should().Throw<NotAuthorisedException>().WithMessage("*signed in*");
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public void ARequirementOnTheInterfaceCoversEveryMethodOnIt()
    {
        var inner = new Spy();
        var guarded = RoleAuthorisation.Decorate<IWholeServiceIsOwnerOnly>(inner, Cashier());

        // Neither method carries the attribute itself. Declaring it once on the interface is
        // what stops a method added later from shipping unguarded.
        FluentActions.Invoking(guarded.First).Should().Throw<NotAuthorisedException>();
        FluentActions.Invoking(guarded.Second).Should().Throw<NotAuthorisedException>();
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public void ARequirementOnTheImplementationIsHonouredToo()
    {
        // The attribute belongs on the interface, but a requirement declared only on the class
        // must not be quietly dropped - the higher of the two always wins.
        var guarded = RoleAuthorisation.Decorate<IUnmarkedService>(new OwnerOnlyImplementation(), Cashier());

        FluentActions.Invoking(guarded.Work).Should().Throw<NotAuthorisedException>();
    }

    [Fact]
    public void NFR_S2_AnAsyncRefusalThrowsRatherThanReturningAFaultedTask()
    {
        var inner = new Spy();
        var guarded = RoleAuthorisation.Decorate<IMixedService>(inner, Cashier());

        // Not awaited: a refusal must not be something a caller who forgot to await can carry on
        // past. Calling the method is what throws.
        Action call = () => _ = guarded.OwnerOnlyAsync();

        call.Should().Throw<NotAuthorisedException>();
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task WhatTheServiceThrowsIsWhatTheCallerCatches()
    {
        var guarded = RoleAuthorisation.Decorate<IMixedService>(new Spy(), Owner());

        // Unwrapped from the TargetInvocationException the reflective call would otherwise wrap
        // it in: a caller must not have to know it was talking to a proxy in order to catch a
        // business rule.
        var call = async () => await guarded.OwnerOnlyAsync();

        await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("the shop said no");
    }

    [Fact]
    public void OnlyAnInterfaceCanBeGuarded()
    {
        var decorate = () => RoleAuthorisation.Decorate(new Spy(), Owner());

        decorate.Should().Throw<ArgumentException>(
            "a concrete class can be called around the guard, so wrapping one would be a false sense of safety");
    }

    private static FakeSession Cashier() =>
        new(new AuthenticatedUser(2, "priya", "Priya", Role.Cashier));

    private static FakeSession Owner() =>
        new(new AuthenticatedUser(1, "owner", "Shop Owner", Role.Owner));

    public interface IMixedService
    {
        public void Anyone();

        [RequiresRole(Role.Owner)]
        public void OwnerOnly();

        [RequiresRole(Role.Owner)]
        public Task OwnerOnlyAsync();
    }

    [RequiresRole(Role.Owner)]
    public interface IWholeServiceIsOwnerOnly
    {
        public void First();

        public void Second();
    }

    public interface IUnmarkedService
    {
        public void Work();
    }

    private sealed class Spy : IMixedService, IWholeServiceIsOwnerOnly
    {
        public int Calls { get; private set; }

        public void Anyone() => Calls++;

        public void OwnerOnly() => Calls++;

        public Task OwnerOnlyAsync()
        {
            Calls++;
            throw new InvalidOperationException("the shop said no");
        }

        public void First() => Calls++;

        public void Second() => Calls++;
    }

    [RequiresRole(Role.Owner)]
    private sealed class OwnerOnlyImplementation : IUnmarkedService
    {
        public void Work()
        {
        }
    }

    private sealed class FakeSession : ISession
    {
        public FakeSession(AuthenticatedUser? user) => CurrentUser = user;

        public AuthenticatedUser? CurrentUser { get; }

        public bool IsAuthenticated => CurrentUser is not null;

        public Role? Role => CurrentUser?.Role;

        public long? ShiftId => null;
    }
}
