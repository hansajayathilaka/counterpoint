using System;
using Counterpoint.Application.Abstractions.Backup;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// An <see cref="IBackupTarget"/> operation failed, with enough of a category attached that a
/// caller can show something more useful than "it did not work" (SRS FR-11.5, UI-06, P4-T01;
/// P4-T03's failure-category requirement is built on the same <see cref="Kind"/>).
/// </summary>
public sealed class BackupTargetException : Exception
{
    public BackupTargetException(BackupTargetFailureKind kind, string message)
        : base(message) => Kind = kind;

    public BackupTargetException(BackupTargetFailureKind kind, string message, Exception innerException)
        : base(message, innerException) => Kind = kind;

    /// <summary>Why the operation failed.</summary>
    public BackupTargetFailureKind Kind { get; }
}
