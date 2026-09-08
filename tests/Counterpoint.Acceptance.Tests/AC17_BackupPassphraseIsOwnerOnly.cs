using System;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Acceptance.Tests;

/// <summary>
/// <b>AC-17</b> applied to the backup passphrase specifically. Found in review: a cashier who
/// reached the settings screen could replace the shop's backup passphrase even though the
/// settings save that followed was correctly refused - every backup taken under the old
/// passphrase would have become permanently unrestorable, silently, with no audit row (the
/// passphrase is not an <c>app_setting</c> and sits outside the FR-10.9 trail entirely).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fix is structural, not an ordering trick.</b> <see cref="IBackupPassphraseStore.SetPassphrase"/>
/// carries <see cref="RequiresRoleAttribute"/> in its own right, exactly as <c>ISettings.SaveAsync</c>
/// does, so a cashier is refused by this store directly - reordering the caller would only have
/// closed one window into it. <see cref="BackupPassphraseStore.SetInitialPassphrase"/> is the
/// separate, internal, unattributed seam first run alone uses, because nobody is signed in while
/// the owner account is still being created.
/// </para>
/// </remarks>
public sealed class AC17_BackupPassphraseIsOwnerOnly
{
    [Fact]
    public void AC_17_ACashierCannotReplaceTheBackupPassphrase()
    {
        var store = new InMemoryPassphraseStore();
        store.SetInitialPassphrase("the-owners-passphrase");

        var decorated = Decorate(store, Role.Cashier);

        var replace = () => decorated.SetPassphrase("mallorys-passphrase");

        replace.Should().Throw<NotAuthorisedException>(
            "replacing the backup passphrase is a privileged action (SRS §3.3 ROLE-2, FR-1.6, NFR-S6, AC-17)");
        store.TryGetPassphrase().Should().Be(
            "the-owners-passphrase",
            "a refused write must leave every backup already taken under the old passphrase restorable");
    }

    [Fact]
    public void AC_17_NobodySignedInCannotReplaceItEither()
    {
        var store = new InMemoryPassphraseStore();
        store.SetInitialPassphrase("the-owners-passphrase");

        var decorated = Decorate(store, role: null);

        var replace = () => decorated.SetPassphrase("mallorys-passphrase");

        replace.Should().Throw<NotAuthorisedException>();
        store.TryGetPassphrase().Should().Be("the-owners-passphrase");
    }

    [Fact]
    public void AC_17_TheOwnerCanReplaceIt()
    {
        // The guard has to be a permission check and not a wall: a rule that refuses everybody
        // proves nothing.
        var store = new InMemoryPassphraseStore();
        store.SetInitialPassphrase("the-owners-old-passphrase");

        var decorated = Decorate(store, Role.Owner);
        decorated.SetPassphrase("the-owners-new-passphrase");

        store.TryGetPassphrase().Should().Be("the-owners-new-passphrase");
    }

    [Fact]
    public void AC_17_ReadingWhetherAPassphraseExistsNeedsNoRole()
    {
        // HasPassphrase backs the settings screen's "a passphrase is set" indicator, shown to
        // whoever opens the screen - it must not be gated the way the write is.
        var store = new InMemoryPassphraseStore();
        var decorated = Decorate(store, role: null);

        decorated.HasPassphrase().Should().BeFalse();

        store.SetInitialPassphrase("something");

        decorated.HasPassphrase().Should().BeTrue();
    }

    [Fact]
    public void FirstRunsInternalSeamBypassesTheRoleCheckByDesign()
    {
        // Nobody is signed in while the owner account is still being created, so first run
        // cannot go through the decorated interface at all - it holds the concrete store, the
        // same way FirstRunSetupService does, and calls the seam ISettings.SaveAsAsync mirrors.
        var store = new InMemoryPassphraseStore();

        store.SetInitialPassphrase("the-shops-first-passphrase");

        store.TryGetPassphrase().Should().Be("the-shops-first-passphrase");
    }

    private static IBackupPassphraseStore Decorate(BackupPassphraseStore store, Role? role) =>
        RoleAuthorisation.Decorate<IBackupPassphraseStore>(store, new FakeSession(role));

    private sealed class FakeSession(Role? role) : ISession
    {
        public AuthenticatedUser? CurrentUser { get; } = role is { } held
            ? new AuthenticatedUser(1, "somebody", "Somebody", held)
            : null;

        public bool IsAuthenticated => CurrentUser is not null;

        public Role? Role => CurrentUser?.Role;

        public long? ShiftId => null;
    }

    /// <summary>A fake protected store: an in-memory string, standing in for Credential Manager
    /// or the development file store - what is under test is the authorisation decision in
    /// front of it, not the platform storage.</summary>
    private sealed class InMemoryPassphraseStore : BackupPassphraseStore
    {
        private string? _passphrase;

        public override bool HasPassphrase() => _passphrase is not null;

        public override string? TryGetPassphrase() => _passphrase;

        protected override void Store(string passphrase) => _passphrase = passphrase;
    }
}
