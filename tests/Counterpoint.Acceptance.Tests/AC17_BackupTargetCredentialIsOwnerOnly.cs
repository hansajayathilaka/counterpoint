using System;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Acceptance.Tests;

/// <summary>
/// <b>AC-17</b> applied to an off-site backup target's credential (P4-T01, SRS FR-11.5, NFR-S6) -
/// the same reasoning <see cref="AC17_BackupPassphraseIsOwnerOnly"/> already proves for the backup
/// passphrase, applied to <see cref="IBackupTargetCredentialStore"/>'s keyed store instead.
/// </summary>
public sealed class AC17_BackupTargetCredentialIsOwnerOnly
{
    private const string TargetKey = "backup-target.s3-compatible";

    [Fact]
    public void AC_17_ACashierCannotReplaceAnOffSiteTargetCredential()
    {
        var store = new InMemoryCredentialStore();
        store.SetCredentialDirectly(TargetKey, "the-owners-credential");

        var decorated = Decorate(store, Role.Cashier);

        var replace = () => decorated.SetCredential(TargetKey, "mallorys-credential");

        replace.Should().Throw<NotAuthorisedException>(
            "replacing an off-site backup credential is a privileged action (SRS §3.3 ROLE-2, FR-1.6, NFR-S6, AC-17)");
        store.TryGetCredential(TargetKey).Should().Be(
            "the-owners-credential", "a refused write must leave the existing off-site copy reachable");
    }

    [Fact]
    public void AC_17_ACashierCannotRemoveAnOffSiteTargetCredentialEither()
    {
        var store = new InMemoryCredentialStore();
        store.SetCredentialDirectly(TargetKey, "the-owners-credential");

        var decorated = Decorate(store, Role.Cashier);

        var remove = () => decorated.RemoveCredential(TargetKey);

        remove.Should().Throw<NotAuthorisedException>();
        store.TryGetCredential(TargetKey).Should().Be("the-owners-credential");
    }

    [Fact]
    public void AC_17_NobodySignedInCannotReplaceItEither()
    {
        var store = new InMemoryCredentialStore();
        store.SetCredentialDirectly(TargetKey, "the-owners-credential");

        var decorated = Decorate(store, role: null);

        var replace = () => decorated.SetCredential(TargetKey, "mallorys-credential");

        replace.Should().Throw<NotAuthorisedException>();
        store.TryGetCredential(TargetKey).Should().Be("the-owners-credential");
    }

    [Fact]
    public void AC_17_TheOwnerCanReplaceIt()
    {
        var store = new InMemoryCredentialStore();
        store.SetCredentialDirectly(TargetKey, "the-owners-old-credential");

        var decorated = Decorate(store, Role.Owner);
        decorated.SetCredential(TargetKey, "the-owners-new-credential");

        store.TryGetCredential(TargetKey).Should().Be("the-owners-new-credential");
    }

    [Fact]
    public void AC_17_ReadingWhetherACredentialExistsNeedsNoRole()
    {
        var store = new InMemoryCredentialStore();
        var decorated = Decorate(store, role: null);

        decorated.HasCredential(TargetKey).Should().BeFalse();

        store.SetCredentialDirectly(TargetKey, "something");

        decorated.HasCredential(TargetKey).Should().BeTrue();
    }

    [Fact]
    public void AC_17_EachTargetKeyIsIndependentOfEveryOther()
    {
        // Switching the shop's off-site target and switching back must find the credential that
        // was there before still there (P4-T01's own "keyed, not singular" design).
        var store = new InMemoryCredentialStore();
        var decorated = Decorate(store, Role.Owner);

        decorated.SetCredential("backup-target.google-drive", "drive-credential");
        decorated.SetCredential("backup-target.s3-compatible", "s3-credential");

        decorated.TryGetCredential("backup-target.google-drive").Should().Be("drive-credential");
        decorated.TryGetCredential("backup-target.s3-compatible").Should().Be("s3-credential");
    }

    private static IBackupTargetCredentialStore Decorate(BackupTargetCredentialStore store, Role? role) =>
        RoleAuthorisation.Decorate<IBackupTargetCredentialStore>(store, new FakeSession(role));

    private sealed class FakeSession(Role? role) : ISession
    {
        public AuthenticatedUser? CurrentUser { get; } = role is { } held
            ? new AuthenticatedUser(1, "somebody", "Somebody", held)
            : null;

        public bool IsAuthenticated => CurrentUser is not null;

        public Role? Role => CurrentUser?.Role;

        public long? ShiftId => null;
    }

    /// <summary>A fake protected store: an in-memory dictionary, standing in for Credential Manager
    /// or the development file store - what is under test is the authorisation decision in front
    /// of it, not the platform storage.</summary>
    private sealed class InMemoryCredentialStore : BackupTargetCredentialStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values = new(StringComparer.Ordinal);

        internal void SetCredentialDirectly(string targetKey, string credential) => _values[targetKey] = credential;

        protected override void Store(string targetKey, string credential) => _values[targetKey] = credential;

        protected override void Remove(string targetKey) => _values.Remove(targetKey);

        protected override string? Read(string targetKey) => _values.GetValueOrDefault(targetKey);
    }
}
