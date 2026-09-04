using System;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The configured data directory cannot hold the database safely - it is on a network share,
/// a mapped drive, or inside a cloud sync folder. SQLite's locking does not survive any of
/// those and the database would be corrupted (engineering guide §4.9).
/// </summary>
/// <remarks>
/// The message is written for the shop owner, not for a developer (UI-06): it names the
/// folder, says why it will not do, and says what to use instead.
/// </remarks>
public sealed class InvalidDataDirectoryException : Exception
{
    public InvalidDataDirectoryException()
    {
    }

    public InvalidDataDirectoryException(string message)
        : base(message)
    {
    }

    public InvalidDataDirectoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private InvalidDataDirectoryException(string message, string offendingPath, Exception? innerException)
        : base(message, innerException) => OffendingPath = offendingPath;

    /// <summary>The folder that was rejected. Empty when the exception carries only a message.</summary>
    public string OffendingPath { get; } = string.Empty;

    /// <summary>
    /// Builds the plain-language refusal. <paramref name="reason"/> completes the sentence
    /// "Counterpoint cannot store its data in ... because ...".
    /// </summary>
    public static InvalidDataDirectoryException For(string path, string reason, Exception? innerException = null)
    {
        var message =
            $"Counterpoint cannot store its data in \"{path}\" because {reason} " +
            "The database must live on a folder on this computer's own disk that is not synced to " +
            "the cloud - normally C:\\ProgramData\\Counterpoint. Move or re-point the data folder " +
            "there and start Counterpoint again.";

        return new InvalidDataDirectoryException(message, path, innerException);
    }
}
