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
/// Scoped to exactly what task P3-T10 owns: <c>App.axaml</c> and the Settings tabs it adds a
/// Display tab beside. Every other hand-built window (<c>SalesWindow.axaml</c>,
/// <c>PurchaseOrderWindow.axaml</c>, ...) still has its own hardcoded hex today - that is task
/// P3-T10's own stated risk, an explicit gap this test does not police, closed by
/// P3-T14/P3-T15/P3-T16.
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
