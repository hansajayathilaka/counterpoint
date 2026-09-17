using System;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// Picks the off-site backup target credential store for the host platform: the OS credential
/// store on Windows, the development file store anywhere else. The same split, for the same
/// reasons, as <see cref="BackupPassphraseStoreFactory"/> (SRS FR-11.5, NFR-S6, P4-T01).
/// </summary>
public static class BackupTargetCredentialStoreFactory
{
    public static BackupTargetCredentialStore Create(PosDataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);

        if (OperatingSystem.IsWindows())
        {
            return new WindowsBackupTargetCredentialStore();
        }

        // Linux and macOS are development hosts only (CLAUDE.md "Development platform note").
        return new FileBackupTargetCredentialStore(dataDirectory);
    }
}
