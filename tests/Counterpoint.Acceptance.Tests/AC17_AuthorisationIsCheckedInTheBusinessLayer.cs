using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Acceptance.Tests;

/// <summary>
/// <b>AC-17</b> — "A cashier account cannot view cost prices, margins or owner-only reports,
/// verified at the business-logic layer as well as the UI."
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no UI anywhere in this file, and that is the test.</b> The acceptance criterion
/// is not "the button is hidden"; it is that the refusal happens below the screen. So this calls
/// the Application service the way a rogue caller would - directly, with a cashier session, no
/// viewmodel, no window, no Avalonia - and requires the same refusal.
/// </para>
/// <para>
/// This project can only reference <c>Counterpoint.Domain</c> and
/// <c>Counterpoint.Application</c>, so it structurally cannot reach a screen even if it wanted
/// to. The store and the audit trail are in-memory fakes: what is under test is the
/// authorisation decision, and a real database would only add ways for the test to fail for
/// reasons that are not AC-17.
/// </para>
/// <para>
/// The cost-and-margin half of AC-17 is enforced at the query projection level - the cashier's
/// DTO has no cost field - and lands with the reports that have one to omit (P3-T04, P3-T05).
/// The pattern is already visible here in <see cref="UserSummary"/>, which has no password hash
/// on it for the same reason.
/// </para>
/// </remarks>
public sealed class AC17_AuthorisationIsCheckedInTheBusinessLayer
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 30, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_17_ACashierCallingAnOwnerOnlyServiceIsRefusedWithTheUiBypassed()
    {
        var world = new World(Role.Cashier);
        var service = world.OwnerOnlyService();

        var list = () => service.ListAsync();
        var create = () => service.CreateAsync(
            new CreateUserCommand("mallory", "Mallory", "letmein", Role.Owner));
        var deactivate = () => service.DeactivateAsync(World.OwnerId);
        var reactivate = () => service.ReactivateAsync(World.OwnerId);
        var reset = () => service.ResetPasswordAsync(World.OwnerId, "letmein");

        await list.Should().ThrowAsync<NotAuthorisedException>();
        await create.Should().ThrowAsync<NotAuthorisedException>();
        await deactivate.Should().ThrowAsync<NotAuthorisedException>();
        await reactivate.Should().ThrowAsync<NotAuthorisedException>();
        await reset.Should().ThrowAsync<NotAuthorisedException>();

        world.Store.Writes.Should().BeEmpty(
            "the refusal happens in front of the service, so nothing was read, written or partly done");
        world.Audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AC_17_TheSameCallSucceedsForAnOwner()
    {
        // The guard has to be a permission check and not a wall: a rule that refuses everybody
        // proves nothing.
        var world = new World(Role.Owner);

        var created = await world.OwnerOnlyService().CreateAsync(
            new CreateUserCommand("priya", "Priya", "till2026", Role.Cashier));

        created.Should().BeGreaterThan(0);
        world.Store.Writes.Should().Contain("create:priya");
        world.Audit.Entries.Should().ContainSingle()
            .Which.Action.Should().Be(SecurityAuditActions.UserCreated);
    }

    [Fact]
    public async Task AC_17_NobodySignedInIsRefusedTheSameWay()
    {
        var world = new World(role: null);

        var list = () => world.OwnerOnlyService().ListAsync();

        await list.Should().ThrowAsync<NotAuthorisedException>();
        world.Store.Writes.Should().BeEmpty();
    }

    /// <summary>The Application layer, wired exactly as the composition root wires it.</summary>
    private sealed class World
    {
        internal World(Role? role)
        {
            Session = new FakeSession(role is { } held
                ? new AuthenticatedUser(role == Role.Owner ? 1 : 2, "somebody", "Somebody", held)
                : null);
        }

        internal static long OwnerId => 1;

        internal FakeSession Session { get; }

        internal FakeUserStore Store { get; } = new();

        internal FakeAuditTrail Audit { get; } = new();

        /// <summary>
        /// The decorated service, composed by hand in the same shape the composition root
        /// composes it.
        /// </summary>
        /// <remarks>
        /// What this file proves is the refusal itself, with no UI anywhere near it. That the
        /// container cannot hand out the <em>un</em>decorated service is a different claim and is
        /// proved elsewhere: <c>UserAdministrationService</c> is internal (this project sees it
        /// only through an <c>InternalsVisibleTo</c> seam), it has no registration of its own, and
        /// <c>ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic</c> fails if a
        /// later owner-only service is written public.
        /// </remarks>
        internal IUserAdministration OwnerOnlyService() => RoleAuthorisation.Decorate<IUserAdministration>(
            new UserAdministrationService(
                Store,
                new PasswordHasher(new Argon2Parameters(MemoryKib: 256, Iterations: 1, Parallelism: 1)),
                Audit,
                new ImmediateUnitOfWork(),
                Session,
                new FixedClock()),
            Session);
    }

    private sealed class FakeSession : ISession
    {
        internal FakeSession(AuthenticatedUser? user) => CurrentUser = user;

        public AuthenticatedUser? CurrentUser { get; }

        public bool IsAuthenticated => CurrentUser is not null;

        public Role? Role => CurrentUser?.Role;

        public long? ShiftId => null;
    }

    /// <summary>
    /// Records everything asked of it, so a test can assert that nothing was.
    /// </summary>
    private sealed class FakeUserStore : IUserStore
    {
        private long _nextId = 10;

        internal List<string> Writes { get; } = [];

        public Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            Writes.Add("find:" + username);
            return Task.FromResult<UserRecord?>(null);
        }

        public Task<UserRecord?> FindByIdAsync(long userId, CancellationToken cancellationToken = default)
        {
            Writes.Add("find:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return Task.FromResult<UserRecord?>(
                new UserRecord(userId, "owner", "Shop Owner", "!", Role.Owner, true, 0, null, null));
        }

        public Task<IReadOnlyList<UserRecord>> ListActiveOwnersAsync(CancellationToken cancellationToken = default)
        {
            Writes.Add("owners");
            return Task.FromResult<IReadOnlyList<UserRecord>>([]);
        }

        public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
        {
            Writes.Add("list");
            return Task.FromResult<IReadOnlyList<UserSummary>>([]);
        }

        public Task<long> CreateAsync(NewUser user, CancellationToken cancellationToken = default)
        {
            Writes.Add("create:" + user.Username);
            return Task.FromResult(_nextId++);
        }

        public Task SetPasswordHashAsync(long userId, string passwordHash, CancellationToken cancellationToken = default)
        {
            Writes.Add("password");
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(long userId, bool active, CancellationToken cancellationToken = default)
        {
            Writes.Add("active");
            return Task.CompletedTask;
        }

        public Task RecordSuccessfulSignInAsync(long userId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            Writes.Add("signin");
            return Task.CompletedTask;
        }

        public Task ClearFailedAttemptsAsync(long userId, CancellationToken cancellationToken = default)
        {
            Writes.Add("clear");
            return Task.CompletedTask;
        }

        public Task RecordFailedSignInAsync(long userId, int failedAttempts, DateTimeOffset? lockedUntil, CancellationToken cancellationToken = default)
        {
            Writes.Add("failed");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        internal List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Runs the operation with no transaction. There is no database here to protect.</summary>
    private sealed class ImmediateUnitOfWork : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
