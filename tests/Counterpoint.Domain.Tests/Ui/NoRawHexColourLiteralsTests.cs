using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T10's last "Done when": <c>App.axaml</c> and every <c>Settings/*View.axaml</c> file
/// contain zero raw hex colour literals (SRS UI-13, NFR-U4). A screen names a semantic
/// <c>DynamicResource</c> key from <c>Styles/Tokens.Light.axaml</c> /
/// <c>Styles/Tokens.Dark.axaml</c>; it never writes a colour itself.
/// </summary>
/// <remarks>
/// <para>
/// Originally scoped to exactly what task P3-T10 owned: <c>App.axaml</c> and the Settings tabs it
/// adds a Display tab beside. Task P3-T14 extended this same policing to <c>SalesWindow.axaml</c>
/// and its extracted side panel, <c>SalesSidePanelView.axaml</c> - the two files that carried the
/// "right side panel is not compatible with dark mode" defect this task exists to close.
/// </para>
/// <para>
/// Task P3-T16 closes the gap left to it by name above: <see cref="UI_13_EveryViewAxamlUnderCounterpointUiHasNoRawHexColourLiteral"/>
/// generalises every per-file test in this class into one repository-wide sweep of every
/// <c>*.axaml</c> file under <c>src/Counterpoint.Ui/Views/</c> (recursively - catalogue and
/// settings subfolders included), which is now the authoritative, superset check; the narrower
/// tests above it are kept rather than deleted, as a named record of exactly which screens each
/// earlier task closed out, and because a passing narrower test can never make the broader one
/// fail.
/// </para>
/// <para>
/// The two token files themselves (<c>Styles/Tokens.Light.axaml</c>,
/// <c>Styles/Tokens.Dark.axaml</c>) are where every one of those hex values is required to live,
/// and are deliberately not scanned here - that is their entire job.
/// </para>
/// </remarks>
public sealed class NoRawHexColourLiteralsTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    /// <summary>A <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c> colour, as Avalonia XAML writes one.</summary>
    private static readonly Regex HexColourLiteral = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void UI_13_AppAxamlHasNoRawHexColourLiteral()
    {
        var offenders = FindOffences(AppAxamlFile()).ToList();

        offenders.Should().BeEmpty(
            "App.axaml must reference a semantic DynamicResource key, never a hex literal "
            + "(SRS UI-13, NFR-U4). Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void UI_13_EverySettingsViewAxamlHasNoRawHexColourLiteral()
    {
        var files = SettingsViewFiles();
        var offenders = new List<string>();

        foreach (var file in files)
        {
            offenders.AddRange(FindOffences(file));
        }

        offenders.Should().BeEmpty(
            "every Settings/*View.axaml screen must reference a semantic DynamicResource key from "
            + "Styles/Tokens.Light.axaml/Tokens.Dark.axaml, never a hex literal (SRS UI-13, "
            + "NFR-U4). Offenders: " + string.Join("; ", offenders));

        files.Should().HaveCountGreaterOrEqualTo(
            9, "the eight existing settings tabs plus the Display tab task P3-T10 adds");
    }

    [Fact]
    public void UI_13_SalesWindowAxamlHasNoRawHexColourLiteral()
    {
        var offenders = FindOffences(SalesUiFile("SalesWindow.axaml")).ToList();

        offenders.Should().BeEmpty(
            "SalesWindow.axaml must reference a semantic DynamicResource key, never a hex "
            + "literal (task P3-T14, SRS UI-13, NFR-U4). Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void UI_13_SalesSidePanelViewAxamlHasNoRawHexColourLiteral()
    {
        var offenders = FindOffences(SalesUiFile("SalesSidePanelView.axaml")).ToList();

        offenders.Should().BeEmpty(
            "SalesSidePanelView.axaml - the extracted sales screen side panel - must reference a "
            + "semantic DynamicResource key, never a hex literal (task P3-T14, SRS UI-13, NFR-U4, "
            + "the confirmed \"side panel is not compatible with dark mode\" defect). Offenders: "
            + string.Join("; ", offenders));
    }

    /// <summary>
    /// Task P3-T16's repository-wide gate (SRS UI-13, NFR-U4): every <c>*.axaml</c> file anywhere
    /// under <c>src/Counterpoint.Ui/Views/</c>, walked recursively rather than named one directory
    /// at a time, so a window added after this task still has to pass it. The one exclusion is
    /// <c>Styles/Tokens.*.axaml</c> - outside <c>Views/</c> entirely, so the recursive walk never
    /// reaches it, and it is where every hex value in the whole application is required to live.
    /// </summary>
    [Fact]
    public void UI_13_EveryViewAxamlUnderCounterpointUiHasNoRawHexColourLiteral()
    {
        var files = AllViewAxamlFiles();
        var offenders = new List<string>();

        foreach (var file in files)
        {
            offenders.AddRange(FindOffences(file));
        }

        offenders.Should().BeEmpty(
            "every screen under src/Counterpoint.Ui/Views/ must reference a semantic "
            + "DynamicResource key from Styles/Tokens.Light.axaml/Tokens.Dark.axaml, never a hex "
            + "literal (SRS UI-13, NFR-U4). Offenders: " + string.Join("; ", offenders));

        files.Should().HaveCountGreaterOrEqualTo(
            30, "every catalogue tab/dialog, every settings tab, the sales screen, the back-office "
                + "shell and every other hand-built window live under this one directory tree");
    }

    /// <summary>
    /// Internal, not private: <see cref="AC21_EveryScreenIsLegibleInBothThemes"/> reuses this
    /// exact file walk rather than a second, divergently-scoped copy of it.
    /// </summary>
    internal static FileInfo[] AllViewAxamlFiles()
    {
        var directory = new DirectoryInfo(
            Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Views"));

        directory.Exists.Should().BeTrue("the Views directory must exist at {0}", directory.FullName);

        var files = directory.GetFiles("*.axaml", SearchOption.AllDirectories);

        files.Should().NotBeEmpty("there must be views to police");

        return files;
    }

    /// <summary>
    /// Internal, not private: <see cref="AC21_EveryScreenIsLegibleInBothThemes"/> reuses this
    /// exact offence scan rather than a second, divergently-scoped copy of it.
    /// </summary>
    internal static IEnumerable<string> FindHexOffences(FileInfo file) => FindOffences(file);

    private static IEnumerable<string> FindOffences(FileInfo file)
    {
        var lines = File.ReadAllLines(file.FullName);

        for (var number = 0; number < lines.Length; number++)
        {
            foreach (Match match in HexColourLiteral.Matches(lines[number]))
            {
                yield return $"{file.Name}:{number + 1} \"{match.Value}\"";
            }
        }
    }

    private static FileInfo AppAxamlFile()
    {
        var path = Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "App.axaml");
        var file = new FileInfo(path);

        file.Exists.Should().BeTrue("App.axaml must exist at {0}", path);

        return file;
    }

    private static FileInfo SalesUiFile(string fileName)
    {
        var path = Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Views", fileName);
        var file = new FileInfo(path);

        file.Exists.Should().BeTrue("{0} must exist at {1}", fileName, path);

        return file;
    }

    private static FileInfo[] SettingsViewFiles()
    {
        var directory = new DirectoryInfo(
            Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Views", "Settings"));

        directory.Exists.Should().BeTrue("the Settings views directory must exist at {0}", directory.FullName);

        // *View.axaml, not *.axaml: RestoreWizardWindow.axaml is a window, not one of the tabs
        // this test polices, and code-behind files end in .axaml.cs, not .axaml.
        var files = directory.GetFiles("*View.axaml", SearchOption.TopDirectoryOnly);

        files.Should().NotBeEmpty("there must be settings screens to police");

        return files;
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
