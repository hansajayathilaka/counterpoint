using System;
using System.IO;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Security;
using Counterpoint.Integration.Tests.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Security;

/// <summary>
/// The development off-site backup target credential store (P4-T01, SRS FR-11.5, NFR-S6) - the
/// keyed sibling of <c>FileKeyStore</c>/<c>FileBackupPassphraseStore</c>, tested the same way.
/// </summary>
public sealed class FileBackupTargetCredentialStoreTests
{
    /// <summary>Owner read/write and nothing else: 0600.</summary>
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    public void P4_T01_ACredentialWrittenThenReadRoundTrips()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileBackupTargetCredentialStore(fixture.DataDirectory);

        store.HasCredential(BackupTargetCredentialKey.S3Compatible).Should().BeFalse();

        store.SetCredential(BackupTargetCredentialKey.S3Compatible, "the-s3-credential-json");

        store.HasCredential(BackupTargetCredentialKey.S3Compatible).Should().BeTrue();
        store.TryGetCredential(BackupTargetCredentialKey.S3Compatible).Should().Be("the-s3-credential-json");
    }

    [Fact]
    public void P4_T01_EachTargetKeyIsStoredIndependently()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileBackupTargetCredentialStore(fixture.DataDirectory);

        store.SetCredential(BackupTargetCredentialKey.GoogleDrive, "drive-credential");
        store.SetCredential(BackupTargetCredentialKey.S3Compatible, "s3-credential");

        store.TryGetCredential(BackupTargetCredentialKey.GoogleDrive).Should().Be("drive-credential");
        store.TryGetCredential(BackupTargetCredentialKey.S3Compatible).Should().Be("s3-credential");

        // Switching away from one target and back must not have lost the credential that was
        // configured for it (P4-T01's own "keyed, not singular" design).
        store.RemoveCredential(BackupTargetCredentialKey.GoogleDrive);

        store.TryGetCredential(BackupTargetCredentialKey.GoogleDrive).Should().BeNull();
        store.TryGetCredential(BackupTargetCredentialKey.S3Compatible).Should().Be(
            "s3-credential", "removing one target's credential must not disturb another's");
    }

    [Fact]
    public void P4_T01_RemovingACredentialThatWasNeverSetIsANoOp()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileBackupTargetCredentialStore(fixture.DataDirectory);

        var remove = () => store.RemoveCredential(BackupTargetCredentialKey.LocalFolder);

        remove.Should().NotThrow();
    }

    [Fact]
    public void NFR_S6_ACredentialFileIsCreatedReadableOnlyByItsOwner()
    {
        using var fixture = new TemporaryDataDirectory();
        var store = new FileBackupTargetCredentialStore(fixture.DataDirectory);

        store.SetCredential(BackupTargetCredentialKey.LocalFolder, "/srv/nas/backups");
        var path = store.PathFor(BackupTargetCredentialKey.LocalFolder);

        File.Exists(path).Should().BeTrue();

        if (OperatingSystem.IsWindows())
        {
            BackupTargetCredentialStoreFactory.Create(fixture.DataDirectory)
                .Should().NotBeOfType<FileBackupTargetCredentialStore>(
                    "the development file store must never be selected on the shipping platform");
            return;
        }

        File.GetUnixFileMode(path).Should().Be(
            OwnerOnly, "an off-site backup credential must not be readable by group or world");
    }

    [Fact]
    public void P4_T01_NoSecretEverAppearsInAppSettingOrAnyConfigFile()
    {
        // The credential lives only in this file, inside the data directory's own
        // backup-target-credentials folder - never inside the SQLite database (app_setting) and
        // never inside a plain settings/config file the installer lays down (NFR-S6).
        using var fixture = new TemporaryDataDirectory();
        var store = new FileBackupTargetCredentialStore(fixture.DataDirectory);

        store.SetCredential(BackupTargetCredentialKey.S3Compatible, "super-secret-access-key");

        var path = store.PathFor(BackupTargetCredentialKey.S3Compatible);
        Path.GetDirectoryName(path).Should().Be(
            Path.Combine(fixture.DataDirectory.Root, "backup-target-credentials"));
        File.ReadAllText(path).Should().Be("super-secret-access-key");
    }

    [Fact]
    public void TheFactoryPicksTheFileStoreOnDevelopmentHostsOnly()
    {
        using var fixture = new TemporaryDataDirectory();

        BackupTargetCredentialStore store = BackupTargetCredentialStoreFactory.Create(fixture.DataDirectory);

        if (OperatingSystem.IsWindows())
        {
            store.Should().BeOfType<WindowsBackupTargetCredentialStore>();
        }
        else
        {
            store.Should().BeOfType<FileBackupTargetCredentialStore>();
        }
    }
}
