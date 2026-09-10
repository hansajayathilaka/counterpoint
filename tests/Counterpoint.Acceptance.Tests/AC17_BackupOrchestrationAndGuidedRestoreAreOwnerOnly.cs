using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Acceptance.Tests;

/// <summary>
/// <b>AC-17</b> applied to the two P1-T15 owner-only backup surfaces: the manual "Backup now"
/// button (SRS FR-11.2) and the whole guided restore wizard, including the read-only preview step
/// (SRS FR-11.12, FR-11.13). Both interfaces are called directly here, exactly as
/// <c>Counterpoint.Backup.DependencyInjection.BackupServiceCollectionExtensions</c> decorates
/// them for the composition root - never through a viewmodel or a window - so this is a proof
/// about the service boundary itself, not about a screen happening to hide a button.
/// </summary>
public sealed class AC17_BackupOrchestrationAndGuidedRestoreAreOwnerOnly
{
    [Fact]
    public async Task AC_17_ACashierCannotTriggerAManualBackup()
    {
        var inner = new FakeBackupOrchestrator();
        var decorated = RoleAuthorisation.Decorate<IManualBackupTrigger>(inner, new FakeSession(Role.Cashier));

        var runNow = async () => await decorated.RunNowAsync();

        await runNow.Should().ThrowAsync<NotAuthorisedException>(
            "taking a backup on demand is the owner's 'Backup now' button, not a cashier action (SRS FR-11.2, NFR-S2, AC-17)");
        inner.RunNowCallCount.Should().Be(0, "a refused call must never reach the real implementation");
    }

    [Fact]
    public async Task AC_17_NobodySignedInCannotTriggerAManualBackupEither()
    {
        var inner = new FakeBackupOrchestrator();
        var decorated = RoleAuthorisation.Decorate<IManualBackupTrigger>(inner, new FakeSession(role: null));

        var runNow = async () => await decorated.RunNowAsync();

        await runNow.Should().ThrowAsync<NotAuthorisedException>();
        inner.RunNowCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC_17_TheOwnerCanTriggerAManualBackup()
    {
        // The guard has to be a permission check, not a wall that refuses everybody.
        var inner = new FakeBackupOrchestrator();
        var decorated = RoleAuthorisation.Decorate<IManualBackupTrigger>(inner, new FakeSession(Role.Owner));

        var outcome = await decorated.RunNowAsync();

        outcome.Succeeded.Should().BeTrue();
        inner.RunNowCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AC_17_ACashierCannotEvenPreviewABackupFileForRestore()
    {
        // IGuidedRestoreService carries RequiresRoleAttribute on the interface itself - every
        // member, including the checksum-only preview that asks for no passphrase, is owner-only
        // (SRS FR-11.12, FR-11.13): restoring the shop's whole database is not a decision a
        // cashier gets to start looking into, even one step in.
        var inner = new FakeGuidedRestoreService();
        var decorated = RoleAuthorisation.Decorate<IGuidedRestoreService>(inner, new FakeSession(Role.Cashier));

        var preview = async () => await decorated.PreviewAsync("/some/backup.cpbk");

        await preview.Should().ThrowAsync<NotAuthorisedException>();
        inner.PreviewCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC_17_ACashierCannotRestoreTheDatabaseEitherEvenWithTheRightTypedConfirmation()
    {
        var inner = new FakeGuidedRestoreService();
        var decorated = RoleAuthorisation.Decorate<IGuidedRestoreService>(inner, new FakeSession(Role.Cashier));

        var restore = async () => await decorated.RestoreAsync(
            new GuidedRestoreRequest("/some/backup.cpbk", "whatever-passphrase", GuidedRestoreConfirmation.RequiredPhrase));

        await restore.Should().ThrowAsync<NotAuthorisedException>(
            "restoring the shop's whole database is exactly the kind of action FR-11.13 requires the owner for, "
                + "regardless of how correctly the cashier filled in the wizard's own confirmation step");
        inner.RestoreCallCount.Should().Be(0, "a refused call must never reach the real implementation - nothing is backed up, staged or audited");
    }

    [Fact]
    public async Task AC_17_TheOwnerCanRunTheWholeGuidedRestore()
    {
        var inner = new FakeGuidedRestoreService();
        var decorated = RoleAuthorisation.Decorate<IGuidedRestoreService>(inner, new FakeSession(Role.Owner));

        var preview = await decorated.PreviewAsync("/some/backup.cpbk");
        preview.SchemaVersion.Should().Be("1.0");

        var outcome = await decorated.RestoreAsync(
            new GuidedRestoreRequest("/some/backup.cpbk", "whatever-passphrase", GuidedRestoreConfirmation.RequiredPhrase));

        outcome.SafetyBackupFilename.Should().Be("safety.cpbk");
        inner.PreviewCallCount.Should().Be(1);
        inner.RestoreCallCount.Should().Be(1);
    }

    private sealed class FakeSession(Role? role) : ISession
    {
        public AuthenticatedUser? CurrentUser { get; } = role is { } held
            ? new AuthenticatedUser(1, "somebody", "Somebody", held)
            : null;

        public bool IsAuthenticated => CurrentUser is not null;

        public Role? Role => CurrentUser?.Role;

        public long? ShiftId => null;
    }

    /// <summary>What is under test is the authorisation decision in front of these services, not
    /// their real behaviour - so both fakes just count calls and return a fixed, valid answer.</summary>
    private sealed class FakeBackupOrchestrator : IManualBackupTrigger
    {
        public int RunNowCallCount { get; private set; }

        public Task<BackupOutcome> RunNowAsync(CancellationToken cancellationToken = default)
        {
            RunNowCallCount++;
            return Task.FromResult(new BackupOutcome(
                BackupTrigger.Manual, Succeeded: true, "backup.cpbk", DateTimeOffset.UtcNow, "NA", null, null));
        }
    }

    private sealed class FakeGuidedRestoreService : IGuidedRestoreService
    {
        public int PreviewCallCount { get; private set; }

        public int RestoreCallCount { get; private set; }

        public Task<GuidedRestorePreview> PreviewAsync(string backupFilePath, CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            return Task.FromResult(new GuidedRestorePreview(DateTimeOffset.UtcNow, "1.0"));
        }

        public Task<GuidedRestoreOutcome> RestoreAsync(GuidedRestoreRequest request, CancellationToken cancellationToken = default)
        {
            RestoreCallCount++;
            return Task.FromResult(new GuidedRestoreOutcome("safety.cpbk", "1.0", DateTimeOffset.UtcNow));
        }
    }
}
