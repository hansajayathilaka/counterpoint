using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Backup.Targets;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// <see cref="LocalFolderTarget"/> against a real, temporary directory - the one implementation
/// P4-T01 can prove end to end without a fake, per its own "Done when": "each target uploads,
/// lists and downloads a test file".
/// </summary>
public sealed class LocalFolderTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "counterpoint-backup-target-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task P4_T01_UploadsListsAndDownloadsATestFile()
    {
        var target = new LocalFolderTarget(_root);
        var content = Encoding.UTF8.GetBytes("a test backup's bytes");

        await target.UploadAsync(
            new MemoryStream(content), "2026/09/17/backup.cpbak",
            new Dictionary<string, string> { ["schemaVersion"] = "42" });

        var listed = await target.ListAsync("2026/");
        listed.Should().ContainSingle(o => o.Key == "2026/09/17/backup.cpbak" && o.SizeBytes == content.Length);

        await using var downloaded = await target.DownloadAsync("2026/09/17/backup.cpbak");
        using var buffer = new MemoryStream();
        await downloaded.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(content, "a downloaded object must be byte-identical to what was uploaded");
    }

    [Fact]
    public async Task P4_T01_ListingRespectsThePrefix()
    {
        var target = new LocalFolderTarget(_root);

        await target.UploadAsync(new MemoryStream([1]), "2026/09/16/a.cpbak", new Dictionary<string, string>());
        await target.UploadAsync(new MemoryStream([2]), "2026/09/17/b.cpbak", new Dictionary<string, string>());
        await target.UploadAsync(new MemoryStream([3]), "other/c.cpbak", new Dictionary<string, string>());

        var listed = await target.ListAsync("2026/");

        listed.Should().HaveCount(2);
        listed.Should().Contain(o => o.Key == "2026/09/16/a.cpbak");
        listed.Should().Contain(o => o.Key == "2026/09/17/b.cpbak");
    }

    [Fact]
    public async Task P4_T01_UploadOverwritesAnExistingObjectAtTheSameKey()
    {
        var target = new LocalFolderTarget(_root);

        await target.UploadAsync(new MemoryStream([1, 2, 3]), "backup.cpbak", new Dictionary<string, string>());
        await target.UploadAsync(new MemoryStream([9, 9]), "backup.cpbak", new Dictionary<string, string>());

        await using var downloaded = await target.DownloadAsync("backup.cpbak");
        using var buffer = new MemoryStream();
        await downloaded.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal([9, 9]);
    }

    [Fact]
    public async Task P4_T01_DeleteRemovesTheObjectButDeleteIsNeverCalledAutomaticallyByAnythingInThisTask()
    {
        // This test exercises DeleteAsync directly, standing in for the future portal pruning job
        // (P4-T04) - proving the method works is not the same as wiring an automatic caller to
        // it, which this task's own instructions say must not happen here. Nothing in
        // Counterpoint.Backup's DI wiring (BackupServiceCollectionExtensions) calls DeleteAsync.
        var target = new LocalFolderTarget(_root);
        await target.UploadAsync(new MemoryStream([1]), "gone.cpbak", new Dictionary<string, string>());

        await target.DeleteAsync("gone.cpbak");

        var listed = await target.ListAsync(string.Empty);
        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task P4_T01_DownloadingAMissingKeyFailsClearly()
    {
        var target = new LocalFolderTarget(_root);

        var download = () => target.DownloadAsync("does-not-exist.cpbak");

        await download.Should().ThrowAsync<BackupTargetException>()
            .Where(ex => ex.Kind == BackupTargetFailureKind.Other);
    }

    [Fact]
    public async Task P4_T01_TestConnectionSucceedsForAWritableFolderAndLeavesNoProbeFileBehind()
    {
        var target = new LocalFolderTarget(_root);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeTrue();
        Directory.GetFiles(_root).Should().BeEmpty("the connection probe must clean up after itself");
    }

    [Fact]
    public async Task P4_T01_TestConnectionFailsClearlyWhenTheFolderCannotBeCreated()
    {
        // A file where a directory is expected can never become a directory - a deterministic,
        // Linux-provable stand-in for "the folder cannot be written to" without touching real
        // permissions.
        var parent = Path.Combine(Path.GetTempPath(), "counterpoint-backup-target-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var blockingFile = Path.Combine(parent, "blocked");
        await File.WriteAllTextAsync(blockingFile, "not a directory");

        var target = new LocalFolderTarget(blockingFile);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(BackupTargetFailureKind.Other);
        result.Message.Should().NotBeNullOrWhiteSpace();

        Directory.Delete(parent, recursive: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
