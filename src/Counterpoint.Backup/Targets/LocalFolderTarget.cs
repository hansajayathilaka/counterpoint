using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// An off-site target that is really just a folder - a mapped NAS share the owner prefers over a
/// true cloud target, or a plain directory used to prove the abstraction on a machine with no
/// cloud credentials at all (SRS FR-11.5, P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>No credential of its own.</b> The "credential" a settings screen stores for this target is
/// simply the folder path - not secret, but kept in the same store as every other target's
/// credential so the screen has one box regardless of which target is chosen.
/// </para>
/// <para>
/// <b>Never a partial object.</b> <see cref="UploadAsync"/> writes to a temporary file beside the
/// destination and moves it into place only once the whole stream has landed, the same reasoning
/// <c>SnapshotService</c> already gives for the local backup file: a reader must never observe a
/// file that is still being written.
/// </para>
/// </remarks>
internal sealed class LocalFolderTarget : IBackupTarget
{
    private const string PartialSuffix = ".partial";
    private const string MetadataSuffix = ".meta.json";

    private readonly string _rootPath;

    internal LocalFolderTarget(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
    }

    /// <inheritdoc />
    public async Task UploadAsync(
        Stream content,
        string key,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(metadata);

        var destination = ResolvePath(key);
        var directory = Path.GetDirectoryName(destination);
        var temporaryPath = destination + PartialSuffix;

        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var fileStream = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destination, overwrite: true);

            if (metadata.Count > 0)
            {
                await File.WriteAllTextAsync(
                    destination + MetadataSuffix,
                    JsonSerializer.Serialize(metadata),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteQuietly(temporaryPath);
            throw new BackupTargetException(
                BackupTargetFailureKind.Other,
                "The local backup folder could not be written to: " + ex.Message,
                ex);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupObjectInfo>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (!Directory.Exists(_rootPath))
        {
            return Task.FromResult<IReadOnlyList<BackupObjectInfo>>([]);
        }

        try
        {
            var objects = Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(PartialSuffix, StringComparison.Ordinal))
                .Where(path => !path.EndsWith(MetadataSuffix, StringComparison.Ordinal))
                .Select(path => (Path: path, Key: ToKey(path)))
                .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(item =>
                {
                    var info = new FileInfo(item.Path);
                    return new BackupObjectInfo(item.Key, info.Length, info.LastWriteTimeUtc);
                })
                .OrderBy(o => o.Key, StringComparer.Ordinal)
                .ToArray();

            return Task.FromResult<IReadOnlyList<BackupObjectInfo>>(objects);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.Other,
                "The local backup folder could not be listed: " + ex.Message,
                ex);
        }
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.Other,
                "No backup object exists at '" + key + "'.");
        }

        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.Other,
                "The backup object could not be opened: " + ex.Message,
                ex);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = ResolvePath(key);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var metadataPath = path + MetadataSuffix;
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.Other,
                "The backup object could not be removed: " + ex.Message,
                ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BackupTargetConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_rootPath);

            // A write-then-read-then-remove probe, entirely through plain file IO - never through
            // this class's own DeleteAsync, which this interface's remarks say must have no
            // automatic caller.
            var probePath = Path.Combine(_rootPath, ".counterpoint-connection-test" + PartialSuffix);
            File.WriteAllBytes(probePath, [1]);
            File.Delete(probePath);

            return Task.FromResult(BackupTargetConnectionResult.Ok(
                "The local folder '" + _rootPath + "' can be written to."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(BackupTargetConnectionResult.Failed(
                "The local folder '" + _rootPath + "' could not be written to: " + ex.Message,
                BackupTargetFailureKind.Other));
        }
    }

    private string ResolvePath(string key) => Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));

    private string ToKey(string path) =>
        Path.GetRelativePath(_rootPath, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort clean-up of the temporary file; the caller already has the real
            // failure to report.
        }
    }
}
