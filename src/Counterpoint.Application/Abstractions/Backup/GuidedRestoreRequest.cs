namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>One guided restore request (SRS FR-11.12).</summary>
/// <param name="BackupFilePath">
/// Full path of the chosen backup file - local or an attached USB drive. A downloaded cloud copy
/// is Phase 4's (FR-11.10).
/// </param>
/// <param name="Passphrase">The passphrase the backup was encrypted under.</param>
/// <param name="TypedConfirmation">
/// What the owner typed. Must equal <see cref="GuidedRestoreConfirmation.RequiredPhrase"/>
/// exactly, or nothing is changed.
/// </param>
public sealed record GuidedRestoreRequest(string BackupFilePath, string Passphrase, string TypedConfirmation);
