using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Backup.Targets;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// <see cref="BackupTargetFactory"/> and <see cref="BackupTargetConnectionTester"/> - the seam
/// P4-T01's own "Done when" means by "switching targets in settings does not require a restart":
/// nothing here is built once and cached, so the very next call sees whatever the credential
/// store and the chosen enum value say now.
/// </summary>
public sealed class BackupTargetFactoryTests : IDisposable
{
    private readonly string _localFolder = Path.Combine(
        Path.GetTempPath(), "counterpoint-backup-target-factory-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task P4_T01_SwitchingTheChosenTargetTakesEffectOnTheVeryNextCallWithNoCachingInBetween()
    {
        var credentials = new InMemoryCredentialStore();
        credentials.SetCredential(BackupTargetCredentialKey.LocalFolder, _localFolder);

        var tester = new BackupTargetConnectionTester(
            new BackupTargetFactory(new FakeHttpMessageHandler().ToHttpClient(), credentials));

        // Before any Google Drive credential exists, the same tester refuses it with a named
        // reason - not the local folder's success, and not a crash.
        var beforeSwitch = await tester.TestConnectionAsync(CloudBackupTarget.GoogleDrive);
        beforeSwitch.Success.Should().BeFalse();
        beforeSwitch.FailureKind.Should().Be(BackupTargetFailureKind.ConfigurationError);

        // Switching the target (what the settings screen's combo box does) and testing again,
        // still through the one long-lived tester instance a singleton composition root would
        // hand the screen - no restart, no new instance.
        var afterSwitch = await tester.TestConnectionAsync(CloudBackupTarget.LocalFolder);
        afterSwitch.Success.Should().BeTrue();
    }

    [Fact]
    public async Task P4_T01_ACredentialOverrideIsUsedInsteadOfWhateverIsStored()
    {
        var credentials = new InMemoryCredentialStore();
        credentials.SetCredential(BackupTargetCredentialKey.LocalFolder, "/does/not/exist/and/cannot/be/created" + Guid.NewGuid());

        var tester = new BackupTargetConnectionTester(
            new BackupTargetFactory(new FakeHttpMessageHandler().ToHttpClient(), credentials));

        // The stored credential is a bad path; the override is a real, writable one - proving the
        // owner can try a freshly typed credential before saving it (SRS FR-11.5, P4-T01).
        var result = await tester.TestConnectionAsync(CloudBackupTarget.LocalFolder, _localFolder);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task P4_T01_NoTargetChosenIsAConfigurationErrorNotAGenericFailure()
    {
        var credentials = new InMemoryCredentialStore();
        var tester = new BackupTargetConnectionTester(
            new BackupTargetFactory(new FakeHttpMessageHandler().ToHttpClient(), credentials));

        var result = await tester.TestConnectionAsync(CloudBackupTarget.None);

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(BackupTargetFailureKind.ConfigurationError);
    }

    [Fact]
    public void P4_T01_AMalformedStoredCredentialIsAConfigurationErrorNotAnUnhandledException()
    {
        var credentials = new InMemoryCredentialStore();
        credentials.SetCredential(BackupTargetCredentialKey.S3Compatible, "not valid json at all");

        var factory = new BackupTargetFactory(new FakeHttpMessageHandler().ToHttpClient(), credentials);

        var create = () => factory.Create(CloudBackupTarget.S3Compatible);

        create.Should().Throw<BackupTargetException>()
            .Where(ex => ex.Kind == BackupTargetFailureKind.ConfigurationError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_localFolder))
        {
            Directory.Delete(_localFolder, recursive: true);
        }
    }

    /// <summary>A fake protected store: an in-memory dictionary, standing in for Credential Manager or the development file store.</summary>
    private sealed class InMemoryCredentialStore : IBackupTargetCredentialStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool HasCredential(string targetKey) => _values.ContainsKey(targetKey);

        public void SetCredential(string targetKey, string credential) => _values[targetKey] = credential;

        public void RemoveCredential(string targetKey) => _values.Remove(targetKey);

        public string? TryGetCredential(string targetKey) => _values.GetValueOrDefault(targetKey);
    }
}
