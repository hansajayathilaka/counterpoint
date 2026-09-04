using System;
using System.Diagnostics;
using System.IO;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// Runs the stock <c>sqlite3</c> command line tool - the plain SQLite tool the till's owner,
/// an auditor or a thief would actually reach for. Used to prove that an encrypted database
/// is unreadable by anything that does not hold the key (NFR-S3).
/// </summary>
/// <remarks>
/// This deliberately shells out to the real binary rather than opening the file with
/// <c>Microsoft.Data.Sqlite</c> and no <c>PRAGMA key</c>. The in-process route links the very
/// SQLCipher library under test, so it proves nothing about a stock build of SQLite.
/// </remarks>
internal static class SqliteCli
{
    /// <summary>Generous: the tool has one file to fail to open.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Full path of the <c>sqlite3</c> binary, or null when it is not installed. CI installs it
    /// (<c>.github/workflows/ci.yml</c>), so a null here on a build machine is a broken image,
    /// not a reason to pass the test quietly.
    /// </summary>
    internal static string? ExecutablePath { get; } = Locate();

    internal static SqliteCliResult Run(string databasePath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ExecutablePath
            ?? throw new InvalidOperationException(
                "The sqlite3 command line tool is not on PATH. Install it (apt-get install sqlite3); " +
                "this test proves an encrypted database is opaque to a stock SQLite build and " +
                "cannot be replaced by an in-process check.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // -readonly so the tool cannot touch the file it fails to read; -batch so it never
        // stops for input if it somehow does open.
        startInfo.ArgumentList.Add("-readonly");
        startInfo.ArgumentList.Add("-batch");
        startInfo.ArgumentList.Add(databasePath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start \"{executable}\".");

        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)RunTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"\"{executable}\" did not exit within {RunTimeout}.");
        }

        return new SqliteCliResult(process.ExitCode, standardOutput, standardError);
    }

    private static string? Locate()
    {
        var fileName = OperatingSystem.IsWindows() ? "sqlite3.exe" : "sqlite3";
        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not this test's problem.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>What the tool printed and how it exited.</summary>
internal sealed record SqliteCliResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Everything the tool said, whichever stream it chose.</summary>
    internal string AllOutput => StandardOutput + StandardError;
}
