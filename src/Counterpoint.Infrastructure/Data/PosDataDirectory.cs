using System;
using System.IO;
using System.Linq;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Resolves and validates the folder that holds the database (engineering guide §4.9).
/// </summary>
/// <remarks>
/// <para>
/// Windows: <c>%ProgramData%\Counterpoint\</c> - never <c>%ProgramFiles%</c> (file
/// virtualisation), never a network path, never inside a cloud sync root. SQLite's file
/// locking is not honoured over SMB and sync clients copy the file out from under the
/// writer, which corrupts it. The app refuses to start rather than corrupt the till's data.
/// </para>
/// <para>
/// Linux and macOS are development-only hosts (CLAUDE.md "Development platform note"); the
/// product ships on Windows. There the directory falls back to
/// <c>$XDG_DATA_HOME/counterpoint</c> or <c>~/.local/share/counterpoint</c>.
/// </para>
/// </remarks>
public sealed class PosDataDirectory
{
    /// <summary>Folder name under %ProgramData% on Windows.</summary>
    public const string ApplicationFolderName = "Counterpoint";

    /// <summary>The single SQLCipher database file.</summary>
    public const string DatabaseFileName = "counterpoint.db";

    private const string DatabaseFolderName = "db";
    private const string OneDriveTenantPrefix = "OneDrive - ";

    /// <summary>
    /// Path segments that mean "this folder is synced to somebody's cloud". Matched
    /// segment-wise and case-insensitively, so a folder honestly called "Dropbox Receipts"
    /// is not rejected while "Dropbox" is.
    /// </summary>
    private static readonly string[] SyncRootSegments =
    [
        "OneDrive",
        "OneDriveCommercial",
        "OneDriveConsumer",
        "Google Drive",
        "GoogleDrive",
        "My Drive",
        "Dropbox",
        "iCloudDrive",
        "iCloud Drive",
    ];

    private static readonly char[] PathSeparators = ['\\', '/'];

    private PosDataDirectory(string root) => Root = root;

    /// <summary>The validated, absolute data directory.</summary>
    public string Root { get; }

    /// <summary>Folder holding the database file. Created by <see cref="EnsureCreated"/>.</summary>
    public string DatabaseDirectory => Path.Combine(Root, DatabaseFolderName);

    /// <summary>Full path of the SQLCipher database file.</summary>
    public string DatabaseFilePath => Path.Combine(DatabaseDirectory, DatabaseFileName);

    /// <summary>
    /// Resolves the data directory, falling back to the platform default when
    /// <paramref name="overridePath"/> is null or blank.
    /// </summary>
    /// <exception cref="InvalidDataDirectoryException">The path is unusable; the message says why.</exception>
    public static PosDataDirectory Resolve(string? overridePath = null)
    {
        var candidate = string.IsNullOrWhiteSpace(overridePath) ? DefaultRoot() : overridePath;

        // Check the path as written first: a Windows-style UNC or sync path handed to a Linux
        // development box would otherwise be mangled by GetFullPath into something that looks safe.
        Reject(candidate);

        string absolute;
        try
        {
            absolute = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw InvalidDataDirectoryException.For(candidate, "it is not a valid folder path.", ex);
        }

        Reject(absolute);
        RejectNetworkDrive(absolute);

        return new PosDataDirectory(absolute);
    }

    /// <summary>Creates the data directory and the database sub-folder if they do not exist.</summary>
    public PosDataDirectory EnsureCreated()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        return this;
    }

    private static string DefaultRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                throw InvalidDataDirectoryException.For(
                    "%ProgramData%",
                    "Windows did not report a ProgramData folder.");
            }

            return Path.Combine(programData, ApplicationFolderName);
        }

        // Development hosts only. The shipped product is Windows.
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, ApplicationFolderName.ToLowerInvariant());
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw InvalidDataDirectoryException.For(
                "$HOME",
                "this machine reports no home folder, so there is nowhere to keep the database.");
        }

        return Path.Combine(home, ".local", "share", ApplicationFolderName.ToLowerInvariant());
    }

    private static void Reject(string path)
    {
        if (IsUncPath(path))
        {
            throw InvalidDataDirectoryException.For(
                path,
                "it is a folder on another computer. SQLite cannot lock a file over a network " +
                "share, so bills would eventually be lost or the database damaged.");
        }

        var syncSegment = FindSyncRootSegment(path);
        if (syncSegment is not null)
        {
            throw InvalidDataDirectoryException.For(
                path,
                $"it is inside the \"{syncSegment}\" cloud sync folder. The sync program copies " +
                "the database while Counterpoint is writing to it, which corrupts it.");
        }
    }

    private static void RejectNetworkDrive(string path)
    {
        if (IsNetworkDrive(path))
        {
            throw InvalidDataDirectoryException.For(
                path,
                "it is on a mapped network drive. SQLite cannot lock a file over a network " +
                "share, so bills would eventually be lost or the database damaged.");
        }
    }

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal);

    private static string? FindSyncRootSegment(string path) =>
        path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(IsSyncRootSegment);

    private static bool IsSyncRootSegment(string segment) =>
        SyncRootSegments.Contains(segment, StringComparer.OrdinalIgnoreCase) ||
        segment.StartsWith(OneDriveTenantPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for a mapped drive such as <c>Z:\</c>. Guarded so it never throws on Linux, where
    /// there are no drive letters and <see cref="DriveInfo"/> would be asked a meaningless question.
    /// </summary>
    private static bool IsNetworkDrive(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        try
        {
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
