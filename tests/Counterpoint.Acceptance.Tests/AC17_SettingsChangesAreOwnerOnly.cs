using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Acceptance.Tests;

/// <summary>
/// <b>AC-17</b> applied to the settings framework — SRS §3.3 ROLE-2 puts "settings" among the
/// things only the owner may do, FR-1.2 makes that split a requirement, FR-1.6 makes a settings
/// change a privileged action, and NFR-S2 says the refusal is enforced in the business layer and
/// not by hiding a menu item.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no UI anywhere in this file, and that is the test.</b> It calls the decorated
/// Application service the way a rogue caller would - directly, with a cashier session, no
/// viewmodel, no window, no container - and requires the refusal to happen there.
/// </para>
/// <para>
/// <b>The read side is deliberately open, and that is also the test.</b> <c>ISettings</c> is not
/// owner-only end to end: the sale screen reads <c>Financial.DecimalPlaces</c> and the rounding
/// rule on every line, and <c>Program.PrepareDatabaseAsync</c> calls <c>LoadAsync</c> at start-up
/// before any session exists. Only <c>SaveAsync</c> and <c>UpdateAsync</c> carry
/// <see cref="RequiresRoleAttribute"/>, so the last test here proves the reads still work with no
/// session at all - otherwise a later change that moved the attribute onto the interface would
/// lock the till out of its own settings and nothing would catch it.
/// </para>
/// </remarks>
public sealed class AC17_SettingsChangesAreOwnerOnly
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 30, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task AC_17_ACashierCannotChangeASettingWithTheUiBypassed()
    {
        var world = await World.LoadedAsync(Role.Cashier);
        var settings = world.Settings;

        var save = () => settings.SaveAsync(settings.Current with
        {
            Policy = settings.Current.Policy with { ReturnWindowDays = 365 },
        });

        var update = () => settings.UpdateAsync(current => current with
        {
            Financial = current.Financial with { DecimalPlaces = 4 },
        });

        await save.Should().ThrowAsync<NotAuthorisedException>(
            "a cashier may sell and return; the shop's financial rules are the owner's (§3.3 ROLE-2)");
        await update.Should().ThrowAsync<NotAuthorisedException>();

        world.Store.Writes.Should().BeEmpty(
            "the refusal happens in front of the service, so nothing was written or partly done");
        world.Audit.Entries.Should().BeEmpty();
        world.Sequences.Configured.Should().BeEmpty(
            "and number_sequence was not touched either (FR-10.4)");
        world.Sequences.Initialised.Should().BeEmpty(
            "the settings path never initialises a counter - that is first run's alone");
        settings.Policy.ReturnWindowDays.Should().Be(
            SettingDefaults.Policy.ReturnWindowDays,
            "the cached snapshot is untouched as well - a refusal is not a half-applied change");
    }

    [Fact]
    public async Task AC_17_TheSameChangeSucceedsForAnOwner()
    {
        // The guard has to be a permission check and not a wall: a rule that refuses everybody
        // proves nothing.
        var world = await World.LoadedAsync(Role.Owner);

        var saved = await world.Settings.UpdateAsync(current => current with
        {
            Policy = current.Policy with { ReturnWindowDays = 30 },
        });

        saved.Policy.ReturnWindowDays.Should().Be(30);
        world.Settings.Policy.ReturnWindowDays.Should().Be(30, "the change is in force");
        world.Store.Writes.Should().Contain("policy.return_window_days");
        world.Audit.Entries.Should().NotBeEmpty("every settings change is audited (FR-10.9)");
    }

    [Fact]
    public async Task AC_17_NobodySignedInIsRefusedTheSameWayACashierIs()
    {
        var world = await World.LoadedAsync(role: null);

        var save = () => world.Settings.SaveAsync(world.Settings.Current with
        {
            Shop = world.Settings.Shop with { Name = "Mallory Hardware" },
        });

        await save.Should().ThrowAsync<NotAuthorisedException>();
        world.Store.Writes.Should().BeEmpty();
        world.Audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AC_17_ReadingASettingNeedsNoSessionAtAll()
    {
        // Start-up order: Program.PrepareDatabaseAsync loads the settings after the migrations and
        // before the login window, so there is no session yet. The sale screen then reads the
        // decimal places and the rounding rule on every line, as a cashier.
        var world = World.Unloaded(role: null);

        var loaded = await world.Settings.LoadAsync();

        loaded.Should().NotBeNull();
        world.Settings.Current.Should().Be(loaded);
        world.Settings.Financial.DecimalPlaces.Should().Be(SettingDefaults.Financial.DecimalPlaces);
        world.Settings.Financial.RoundingRule.Should().Be(SettingDefaults.Financial.RoundingRule);
        world.Settings.Policy.NegativeStock.Should().Be(SettingDefaults.Policy.NegativeStock);
        world.Settings.Shop.Name.Should().Be(SettingDefaults.Shop.Name);
        world.Settings.Tax.TaxLabel.Should().Be(SettingDefaults.Tax.TaxLabel);
        world.Settings.Numbering.Bill.Prefix.Should().Be(SettingDefaults.Numbering.Bill.Prefix);
        world.Settings.Peripherals.PaperWidthMm.Should().Be(SettingDefaults.Peripherals.PaperWidthMm);
        world.Settings.Backup.RetentionDays.Should().Be(SettingDefaults.Backup.RetentionDays);
        world.Settings.Receipt.FooterText.Should().Be(SettingDefaults.Receipt.FooterText);

        // And as a cashier, which is who is actually standing there.
        var cashier = World.Unloaded(Role.Cashier);
        await cashier.Settings.LoadAsync();
        cashier.Settings.Financial.DecimalPlaces.Should().Be(SettingDefaults.Financial.DecimalPlaces);
    }

    /// <summary>The Application layer, wired exactly as the composition root wires it.</summary>
    private sealed class World
    {
        private World(Role? role)
        {
            Session = new FakeSession(role is { } held
                ? new AuthenticatedUser(role == Role.Owner ? 1 : 2, "somebody", "Somebody", held)
                : null);

            // The decorated interface, composed by hand in the same shape
            // CounterpointHostBuilderExtensions.AddCounterpointSettings composes it. That the
            // container cannot hand out the undecorated SettingsService is a different claim,
            // proved by the class being internal (this project sees it only through an
            // InternalsVisibleTo seam) and by
            // ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic.
            Settings = RoleAuthorisation.Decorate<ISettings>(
                new SettingsService(
                    Store,
                    new ImmediateUnitOfWork(),
                    Audit,
                    Session,
                    new FakePassphraseStore(),
                    Sequences,
                    new FixedClock()),
                Session);
        }

        internal FakeSession Session { get; }

        internal FakeSettingStore Store { get; } = new();

        internal FakeAuditTrail Audit { get; } = new();

        internal FakeNumberSequences Sequences { get; } = new();

        internal ISettings Settings { get; }

        internal static World Unloaded(Role? role) => new(role);

        /// <summary>
        /// The world with the settings already read, which is the state the till is in from
        /// start-up onwards. The load itself carries no role requirement, so it does not matter
        /// who - if anyone - is signed in when it runs.
        /// </summary>
        internal static async Task<World> LoadedAsync(Role? role)
        {
            var world = new World(role);
            await world.Settings.LoadAsync();
            world.Store.Writes.Clear();

            return world;
        }
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
    /// An empty table that records every key written to it, so a test can assert that nothing was.
    /// </summary>
    private sealed class FakeSettingStore : ISettingStore
    {
        internal List<string> Writes { get; } = [];

        public Task<IReadOnlyDictionary<string, StoredSetting>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, StoredSetting>>(
                new Dictionary<string, StoredSetting>(StringComparer.Ordinal));

        public Task WriteAsync(
            IReadOnlyList<SettingWrite> writes,
            CancellationToken cancellationToken = default)
        {
            foreach (var write in writes)
            {
                Writes.Add(write.Key);
            }

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

    private sealed class FakeNumberSequences : INumberSequenceConfiguration
    {
        internal List<string> Configured { get; } = [];

        /// <summary>Anything the settings path asked to initialise, which must be nothing.</summary>
        internal List<string> Initialised { get; } = [];

        public Task<bool> ConfigureAsync(
            string documentType,
            string prefix,
            string pattern,
            long startingNumber,
            CancellationToken cancellationToken = default)
        {
            Configured.Add(documentType);
            return Task.FromResult(true);
        }

        public Task<bool> InitialiseAsync(
            string documentType,
            string prefix,
            string pattern,
            long startingNumber,
            CancellationToken cancellationToken = default)
        {
            Initialised.Add(documentType);
            return Task.FromResult(true);
        }
    }

    private sealed class FakePassphraseStore : IBackupPassphraseStore
    {
        public bool HasPassphrase() => false;

        public void SetPassphrase(string passphrase)
        {
        }

        public string? TryGetPassphrase() => null;
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
