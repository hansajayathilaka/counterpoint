using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T13's file-level proofs (SRS UI-11, NFR-S2, AC-17, AC-24): the flat back-office button
/// row is gone from <c>SalesWindow.axaml</c>, replaced by one entry point, and
/// <c>BackOfficeShellWindow.axaml</c> is visually distinct from it - its own status bar, its own
/// accent, drawn from the P3-T10 tokens rather than a raw hex literal - the same file-inspection
/// style <c>NoRawHexColourLiteralsTests</c> and <c>EditDialogFrameworkTests</c> already use to
/// prove a screen without opening a window (a window cannot be opened in CI).
/// </summary>
public sealed class BackOfficeShellTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    private static readonly Regex HexColourLiteral = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void UI_11_SalesWindowNoLongerHostsTheFlatBackOfficeButtonRow()
    {
        var markup = ReadUiFile("Views", "SalesWindow.axaml");

        // The confirmed shape before this task: five separate buttons, gated one flag each,
        // sitting directly on the sales screen with the same chrome as everything else on it.
        markup.Should().NotContain("Command=\"{Binding ManageUsersCommand}\"");
        markup.Should().NotContain("Command=\"{Binding ManageCatalogueCommand}\"");
        markup.Should().NotContain("Command=\"{Binding PrintLabelsCommand}\"");
        markup.Should().NotContain("Command=\"{Binding ManagePurchaseOrdersCommand}\"");
        markup.Should().NotContain("Command=\"{Binding OpenSettingsCommand}\"");

        // Replaced by the one entry point (P3-T13 "Do this" #2).
        markup.Should().Contain("Command=\"{Binding OpenBackOfficeCommand}\"");
        markup.Should().Contain("IsVisible=\"{Binding CanOpenBackOffice}\"");
    }

    [Fact]
    public void UI_11_BackOfficeShellWindowHasItsOwnDistinctAccentAndStatusBar()
    {
        var markup = ReadUiFile("Views", "BackOfficeShellWindow.axaml");

        // The distinct colour accent task P3-T13 asks for, drawn from the P3-T10 tokens - not a
        // second theme system, the same AccentBrush key the Light/Dark palettes already declare.
        markup.Should().Contain("{DynamicResource AccentBrush}");

        // Its own status bar: a different background token from SalesWindow's raw #1F2937, and
        // its own permanent label naming the screen, so the two windows read as two different
        // places even before a cashier reads a single word of content.
        markup.Should().Contain("{DynamicResource PanelBackgroundBrush}");
        markup.Should().Contain("BACK OFFICE");

        // The five destinations this shell, and only this shell, now navigates to.
        markup.Should().Contain("Command=\"{Binding ManageCatalogueCommand}\"");
        markup.Should().Contain("Command=\"{Binding OpenSettingsCommand}\"");
        markup.Should().Contain("Command=\"{Binding ManageUsersCommand}\"");
        markup.Should().Contain("Command=\"{Binding ManagePurchaseOrdersCommand}\"");
        markup.Should().Contain("Command=\"{Binding PrintLabelsCommand}\"");
    }

    [Fact]
    public void UI_13_BackOfficeShellWindowHasNoRawHexColourLiteral()
    {
        var markup = ReadUiFile("Views", "BackOfficeShellWindow.axaml");

        var offenders = new List<string>();
        var lines = markup.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in HexColourLiteral.Matches(lines[i]))
            {
                offenders.Add($"BackOfficeShellWindow.axaml:{i + 1} \"{match.Value}\"");
            }
        }

        offenders.Should().BeEmpty(
            "BackOfficeShellWindow.axaml must reference a semantic DynamicResource key from "
            + "Styles/Tokens.Light.axaml/Tokens.Dark.axaml, never a hex literal (SRS UI-13, "
            + "NFR-U4). Offenders: " + string.Join("; ", offenders));
    }

    private static string ReadUiFile(params string[] relativeSegments)
    {
        var path = Path.Combine(
            RepositoryRoot().FullName,
            "src",
            "Counterpoint.Ui",
            Path.Combine(relativeSegments));

        File.Exists(path).Should().BeTrue("expected {0} to exist", path);

        return File.ReadAllText(path);
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
