namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// The typed confirmation FR-11.12 asks for - "require explicit typed confirmation" - not merely
/// a button click. One constant, so the screen's prompt and <see cref="IGuidedRestoreService"/>'s
/// check can never drift apart.
/// </summary>
public static class GuidedRestoreConfirmation
{
    /// <summary>What the owner must type, exactly, before a restore proceeds. Case-sensitive.</summary>
    public const string RequiredPhrase = "RESTORE";
}
