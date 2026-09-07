using System;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// Picks the backup-passphrase store for the host platform: the OS credential store on Windows,
/// the development file store anywhere else. The same split, for the same reasons, as
/// <see cref="DatabaseKeyStoreFactory"/>.
/// </summary>
public static class BackupPassphraseStoreFactory
{
    public static BackupPassphraseStore Create(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);

        if (OperatingSystem.IsWindows())
        {
            return new WindowsBackupPassphraseStore();
        }

        // Linux and macOS are development hosts only (CLAUDE.md "Development platform note").
        return new FileBackupPassphraseStore(dataDirectory);
    }
}
