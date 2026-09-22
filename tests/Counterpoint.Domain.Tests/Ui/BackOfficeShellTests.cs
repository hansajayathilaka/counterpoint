using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T13's file-level proofs (SRS UI-11, NFR-S2, AC-17, AC-24), extended by task P3-T18: the
/// flat back-office button row is gone from <c>SalesWindow.axaml</c>, replaced by one entry point;
/// <c>BackOfficeShellWindow.axaml</c> is visually distinct from it - its own status bar, its own
/// accent, drawn from the P3-T10 tokens rather than a raw hex literal; and the shell's own flat
/// five-tile grid is gone too, replaced by <c>NavRail.axaml</c>'s persistent grouped rail, with
/// Catalogue folded into the content pane instead of opening a window - the same file-inspection
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

        // Task P3-T18: the persistent nav rail, and Catalogue folded straight into this window's
        // own content pane rather than a separate window.
        markup.Should().Contain("<views:NavRail");
        markup.Should().Contain("<catalogue:CatalogueSectionContent");
        markup.Should().Contain("SelectedSection=\"{Binding SelectedCatalogueSection}\"");
    }

    [Fact]
    public void UI_18_BackOfficeShellWindowNoLongerOpensCatalogueAsASeparateWindow()
    {
        var markup = ReadUiFile("Views", "BackOfficeShellWindow.axaml");

        // Catalogue's navigation model changed by task P3-T18: no more ManageCatalogueCommand
        // (retired - it used to raise CatalogueRequested, which App.axaml.cs used to open
        // CatalogueWindow with). The nav rail's own ListBox binds SelectedCatalogueSection
        // instead (asserted by the accent/status-bar test above).
        markup.Should().NotContain("ManageCatalogueCommand");
        markup.Should().NotContain("CatalogueRequested");
    }

    [Fact]
    public void UI_18_NavRailHoldsTheFiveDestinationsGatedByTheSameCanFlagsAsBefore()
    {
        var markup = ReadUiFile("Views", "NavRail.axaml");

        // The five destinations the old flat tile grid gated - the same Can* flag each, per this
        // task's own risk mitigation (task P3-T18's "Do this" #4, "Done when" #1).
        markup.Should().Contain("IsVisible=\"{Binding CanManageCatalogue}\"");
        markup.Should().Contain("Command=\"{Binding OpenSettingsCommand}\"");
        markup.Should().Contain("IsVisible=\"{Binding CanChangeSettings}\"");
        markup.Should().Contain("Command=\"{Binding ManageUsersCommand}\"");
        markup.Should().Contain("IsVisible=\"{Binding CanManageUsers}\"");
        markup.Should().Contain("Command=\"{Binding ManagePurchaseOrdersCommand}\"");
        markup.Should().Contain("IsVisible=\"{Binding CanManagePurchasing}\"");
        markup.Should().Contain("Command=\"{Binding PrintLabelsCommand}\"");
        markup.Should().Contain("IsVisible=\"{Binding CanPrintLabels}\"");

        // Catalogue's eight sections replace TabControl.SelectedIndex with a ListBox bound to
        // SelectedCatalogueSection - the keyboard-nav risk mitigation task P3-T18 names by name.
        markup.Should().Contain("SelectedItem=\"{Binding SelectedCatalogueSection, Mode=TwoWay}\"");
        markup.Should().Contain("{Binding CatalogueSections}");

        // Drawn from the P3-T17 rail sub-palette, not a second theme system.
        markup.Should().Contain("{DynamicResource RailBackgroundBrush}");
        markup.Should().Contain("{DynamicResource RailTextBrush}");
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
