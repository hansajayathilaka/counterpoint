namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// What one call to <see cref="IBackupTargetConnectionTester.TestConnectionAsync"/> found (SRS
/// FR-11.5, P4-T01). A plain result, never an exception - the settings screen shows
/// <see cref="Message"/> and does not interpret it further (SRS UI-06), the same shape
/// <c>BackupOutcome</c> already uses for <c>IManualBackupTrigger</c>.
/// </summary>
/// <param name="Success">Whether the target could be reached and the credential accepted.</param>
/// <param name="Message">
/// A plain-language sentence for the settings screen - what happened, and for a failure, what to
/// do about it. Never the credential itself, and never a raw exception message that might carry
/// one.
/// </param>
/// <param name="FailureKind">
/// Why it failed, when <paramref name="Success"/> is false - null when it succeeded.
/// </param>
public sealed record BackupTargetConnectionResult(
    bool Success,
    string Message,
    BackupTargetFailureKind? FailureKind = null)
{
    /// <summary>A successful connection test.</summary>
    public static BackupTargetConnectionResult Ok(string message) => new(true, message);

    /// <summary>A failed connection test, naming why.</summary>
    public static BackupTargetConnectionResult Failed(string message, BackupTargetFailureKind kind) =>
        new(false, message, kind);
}
