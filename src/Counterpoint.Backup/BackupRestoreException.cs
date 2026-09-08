using System;

namespace Counterpoint.Backup;

/// <summary>
/// A backup could not be restored, for a reason a human operator needs to see rather than a
/// technical crash - a wrong passphrase, a truncated or tampered file, or a database that failed
/// its integrity check (SRS FR-11.4: "a wrong passphrase fails cleanly with a plain-language
/// message").
/// </summary>
public sealed class BackupRestoreException : Exception
{
    public BackupRestoreException()
    {
    }

    public BackupRestoreException(string message)
        : base(message)
    {
    }

    public BackupRestoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
