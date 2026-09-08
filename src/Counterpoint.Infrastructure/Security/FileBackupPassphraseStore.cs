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
/// Keeps the backup passphrase in a file inside the data directory so the backup stack can be
/// built and tested on Linux, where DPAPI and Windows Credential Manager do not exist
/// (CLAUDE.md "Development platform note"). It protects nothing: it is the passphrase, in a file,
/// on the same disk. On Windows the passphrase lives in Credential Manager under DPAPI instead -
/// see <see cref="WindowsBackupPassphraseStore"/>.
/// </para>
/// <para>
/// The installer must never lay this down, and <see cref="BackupPassphraseStoreFactory"/> only
/// selects it off Windows.
/// </para>
/// <para>
/// <b>Internal, like <c>SettingsService</c> and <c>UserAdministrationService</c>.</b>
/// <see cref="IBackupPassphraseStore.SetPassphrase"/> is owner-only and the guard lives in the
/// role-decorated interface the composition root registers; a public store could be constructed or
/// resolved by its concrete type with nothing in front of it, and the write would go straight
/// through (SRS NFR-S2, AC-17).
/// </para>
/// </remarks>
internal sealed class FileBackupPassphraseStore : BackupPassphraseStore
{
    /// <summary>Name of the development passphrase file inside the data directory.</summary>
    public const string PassphraseFileName = "backup.devpassphrase";

    private readonly object _gate = new();
    private readonly string _path;

    internal FileBackupPassphraseStore(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _path = Path.Combine(dataDirectory.Root, PassphraseFileName);
    }

    /// <summary>Full path of the passphrase file. Exposed so a test can assert its permissions.</summary>
    internal string PassphraseFilePath => _path;

    /// <inheritdoc />
    public override bool HasPassphrase()
    {
        lock (_gate)
        {
            return File.Exists(_path) && new FileInfo(_path).Length > 0;
        }
    }

    /// <inheritdoc />
    protected override void Store(string passphrase)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, passphrase, Encoding.UTF8);
            RestrictToOwner(_path);
        }
    }

    /// <inheritdoc />
    public override string? TryGetPassphrase()
    {
        lock (_gate)
        {
            return File.Exists(_path) ? File.ReadAllText(_path, Encoding.UTF8) : null;
        }
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
