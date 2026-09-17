using System;
using System.IO;
using System.Text;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// DEVELOPMENT ONLY - NEVER SHIPPED.
/// </summary>
/// <remarks>
/// <para>
/// Keeps each off-site backup target's credential in its own file inside the data directory so
/// the backup stack can be built and tested on Linux, where DPAPI and Windows Credential Manager
/// do not exist (CLAUDE.md "Development platform note"). It protects nothing: it is the
/// credential, in a file, on the same disk. On Windows each credential lives in Credential
/// Manager under DPAPI instead - see <see cref="WindowsBackupTargetCredentialStore"/>.
/// </para>
/// <para>
/// The installer must never lay this down, and <see cref="BackupTargetCredentialStoreFactory"/>
/// only selects it off Windows.
/// </para>
/// <para>
/// <b>Internal, like <c>FileBackupPassphraseStore</c>.</b>
/// <see cref="IBackupTargetCredentialStore.SetCredential"/> and
/// <see cref="IBackupTargetCredentialStore.RemoveCredential"/> are owner-only and the guard lives
/// in the role-decorated interface the composition root registers; a public store could be
/// constructed or resolved by its concrete type with nothing in front of it, and the write would
/// go straight through (SRS NFR-S2, AC-17).
/// </para>
/// </remarks>
internal sealed class FileBackupTargetCredentialStore : BackupTargetCredentialStore
{
    /// <summary>Filename suffix for a development target-credential file inside the data directory.</summary>
    public const string FileSuffix = ".devcredential";

    private readonly object _gate = new();
    private readonly string _directory;

    internal FileBackupTargetCredentialStore(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _directory = Path.Combine(dataDirectory.Root, "backup-target-credentials");
    }

    /// <summary>Full path of the file a given target key is stored under. Exposed so a test can assert its permissions.</summary>
    internal string PathFor(string targetKey) => Path.Combine(_directory, Sanitise(targetKey) + FileSuffix);

    /// <inheritdoc />
    protected override void Store(string targetKey, string credential)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            var path = PathFor(targetKey);
            File.WriteAllText(path, credential, Encoding.UTF8);
            RestrictToOwner(path);
        }
    }

    /// <inheritdoc />
    protected override void Remove(string targetKey)
    {
        lock (_gate)
        {
            var path = PathFor(targetKey);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <inheritdoc />
    protected override string? Read(string targetKey)
    {
        lock (_gate)
        {
            var path = PathFor(targetKey);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
    }

    /// <summary>
    /// A target key made safe as a file name - the keys this project defines are already plain
    /// ASCII tokens, but a test or a future target must not be able to walk the credential
    /// directory with a key containing a path separator.
    /// </summary>
    private static string Sanitise(string targetKey)
    {
        var builder = new StringBuilder(targetKey.Length);
        foreach (var ch in targetKey)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_');
        }

        return builder.ToString();
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows never uses this store; the installer's ACLs on %ProgramData%\Counterpoint
            // are the control there.
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
