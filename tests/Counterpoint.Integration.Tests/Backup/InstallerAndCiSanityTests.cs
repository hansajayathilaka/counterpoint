using System;
using System.IO;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup;

/// <summary>
/// Cheap, Linux-runnable sanity checks over the two P0-T07 "Done when" items that genuinely need
/// Windows CI to prove for real (docs/02_PHASE_0_walking_skeleton.md P0-T07).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class does not, and cannot, prove.</b> "the self-contained publish runs with the
/// SQLCipher native asset present (Windows CI job green)" and "installer/Counterpoint.iss ...
/// produces an installer artifact in CI" are both steps in the <c>windows-publish</c> job of
/// <c>.github/workflows/ci.yml</c> that only run on <c>windows-2022</c>: a real <c>win-x64</c>
/// self-contained publish and Inno Setup's <c>iscc.exe</c> compiling an Inno Setup script.
/// <c>dotnet test</c> on Linux has neither a Windows publish host nor an Inno Setup interpreter,
/// so it cannot execute either step - that is the CI job's job, and only the CI job's, per
/// CLAUDE.md's hardware-boundary note ("software task ... keeps only the checks a byte-stream
/// snapshot or a fake-failure test can prove on Linux").
/// </para>
/// <para>
/// What plain text comparison against the repository's own files <em>can</em> cheaply catch: both
/// files hard-code facts the compiled solution already knows independently of them - the
/// composition root's own assembly name, and the sub-folder names
/// <see cref="PosDataDirectory"/> actually creates. A hand-edit that drifts either file from those
/// facts (for example, reverting the installer's exe name, or the workflow's publish target,
/// back to <c>Counterpoint.Ui</c>) would otherwise only be caught by a human reading a diff, or
/// not until <c>HW-T06</c>.
/// </para>
/// </remarks>
public sealed class InstallerAndCiSanityTests
{
    [Fact]
    public void InstallerScriptExistsAndAgreesWithTheComposedAppAndItsDataDirectory()
    {
        var iss = File.ReadAllText(FindRepositoryFile("installer", "Counterpoint.iss"));

        // The composition root's own WinExe AssemblyName (Counterpoint.App.csproj), not
        // Counterpoint.Ui - CLAUDE.md "Project boundaries": Counterpoint.Ui is a class library
        // and was never publishable as an executable at all.
        iss.Should().Contain(
            "MyAppExeName \"Counterpoint.exe\"",
            "the installer's shortcut and [Run] entry must launch the assembly the composed "
            + "WinExe actually publishes as");

        var root = Path.Combine(
            Path.GetTempPath(), "counterpoint-tests", "installer-sanity-" + Guid.NewGuid().ToString("N"));

        try
        {
            // PosDataDirectory's own sub-folder names (docs/01_DATA_MODEL.md, engineering guide
            // §4.9) - read off the class itself, not retyped by hand, so the two cannot drift
            // from each other silently.
            var dataDirectory = PosDataDirectory.Resolve(root).EnsureCreated();

            var databaseFolderName = Path.GetFileName(
                dataDirectory.DatabaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var backupFolderName = Path.GetFileName(
                Path.GetDirectoryName(dataDirectory.SnapshotDirectory.TrimEnd(Path.DirectorySeparatorChar))!);

            iss.Should().Contain(
                "{commonappdata}\\{#MyAppName}\\" + databaseFolderName,
                "the [Dirs] section must create the same database sub-folder PosDataDirectory resolves to");
            iss.Should().Contain(
                "{commonappdata}\\{#MyAppName}\\" + backupFolderName,
                "the [Dirs] section must create the same backups sub-folder PosDataDirectory "
                + "resolves to - both the FR-11 snapshots and the pre-migration copy live under it");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CiPublishesTheComposedAppNotTheUiLibrary()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "ci.yml"));

        workflow.Should().Contain(
            "dotnet publish src/Counterpoint.App",
            "Counterpoint.App is the WinExe composition root; Counterpoint.Ui is a class library "
            + "and cannot be published as a self-contained executable at all (CLAUDE.md "
            + "\"Project boundaries\")");
        workflow.Should().NotContain(
            "dotnet publish src/Counterpoint.Ui",
            "the regression this task's own diff fixed - publishing the library rather than the app");
    }

    [Fact]
    public void CiVerifiesTheSqlCipherNativeAssetAndBuildsTheInstaller()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "ci.yml"));

        workflow.Should().Contain(
            "sqlcipher",
            "NFR-S3: the job must actually check the native asset deployed, not merely publish "
            + "and report a size");
        workflow.Should().Contain(
            "installer\\Counterpoint.iss",
            "the workflow must compile the very script this task adds, not a placeholder");
        workflow.Should().Contain(
            "pos-win-x64-installer",
            "the compiled installer must be uploaded as a CI artifact, or 'produces an installer "
            + "artifact in CI' is not actually true");
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Counterpoint.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull(
            "the repository root (Counterpoint.sln) must be found above the test output directory");

        var path = Path.Combine([directory!.FullName, .. relativeSegments]);
        File.Exists(path).Should().BeTrue("expected to find {0}", path);
        return path;
    }
}
