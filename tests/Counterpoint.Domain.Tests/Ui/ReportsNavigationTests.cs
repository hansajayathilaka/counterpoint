using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T05's file-level proofs for the Reports screens (SRS FR-9.1, FR-9.4, UI-11, UI-13, AC-17):
/// the rail offers four Reports items - the two cost-free ones to any signed-in user, Profit and
/// Returns gated to the owner - the shell window hosts the section content, and none of the new
/// markup writes a raw hex colour. The same file-inspection style <see cref="BackOfficeShellTests"/>
/// uses, since a window cannot be opened here.
/// </summary>
public sealed class ReportsNavigationTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    private static readonly Regex HexColourLiteral = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly string[] ReportViewFiles =
    [
        "SalesSummaryReportView.axaml",
        "SalesByItemReportView.axaml",
        "ProfitReportView.axaml",
        "ReturnsReportView.axaml",
        "BillDrillDownView.axaml",
        "ReportRangeBar.axaml",
        "ReportSectionContent.axaml",
    ];

    [Fact]
    public void UI_11_NavRailHoldsTheFourReportItemsUnderOneGroupGatedByCanViewReports()
    {
        var markup = Read("Views", "NavRail.axaml");

        markup.Should().Contain("Text=\"REPORTS\"");
        markup.Should().Contain("IsVisible=\"{Binding CanViewReports}\"");

        foreach (var section in new[] { "Sales summary", "Sales by item", "Profit", "Returns" })
        {
            markup.Should().Contain("CommandParameter=\"" + section + "\"", "the rail must offer {0}", section);
        }

        markup.Should().Contain("Command=\"{Binding SelectReportSectionCommand}\"");
    }

    [Fact]
    public void AC_17_OnlyProfitAndReturnsAreGatedToTheOwnerInTheRailAndTheCostFreeTwoAreNot()
    {
        var markup = Read("Views", "NavRail.axaml");

        ToggleFor(markup, "Profit").Should().Contain("IsVisible=\"{Binding CanViewOwnerReports}\"");
        ToggleFor(markup, "Returns").Should().Contain("IsVisible=\"{Binding CanViewOwnerReports}\"");
        ToggleFor(markup, "Sales summary").Should().NotContain("IsVisible=", "RPT-01 carries no cost or margin field");
        ToggleFor(markup, "Sales by item").Should().NotContain("IsVisible=", "RPT-02 carries no cost or margin field");
    }

    [Fact]
    public void UI_11_TheShellWindowHostsTheReportSectionContentBoundToTheShellsReports()
    {
        var markup = Read("Views", "BackOfficeShellWindow.axaml");

        markup.Should().Contain("reports:ReportSectionContent");
        markup.Should().Contain("Reports=\"{Binding Reports}\"");
        markup.Should().Contain("SelectedSection=\"{Binding SelectedReportSection}\"");
        markup.Should().Contain("IsVisible=\"{Binding IsReportSectionActive}\"");
    }

    [Fact]
    public void UI_13_EveryReportViewMarkupFileExistsAndHasNoRawHexColourLiteral()
    {
        var offenders = new List<string>();

        foreach (var file in ReportViewFiles)
        {
            var lines = Read("Views", "Reports", file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in HexColourLiteral.Matches(lines[i]))
                {
                    offenders.Add($"{file}:{i + 1} \"{match.Value}\"");
                }
            }
        }

        offenders.Should().BeEmpty(
            "the Reports screens must name a semantic DynamicResource token, never a hex literal (SRS UI-13, NFR-U4). "
            + "Offenders: " + string.Join("; ", offenders));
    }

    [Fact]
    public void UI_14_EveryReportViewDeclaresACompiledBindingDataTypeSoABadBindingFailsTheBuildNotTheShop()
    {
        foreach (var file in ReportViewFiles.Where(name => name != "ReportSectionContent.axaml"))
        {
            Read("Views", "Reports", file).Should().Contain("x:DataType=", "{0} must use compiled bindings", file);
        }
    }

    /// <summary>The single ToggleButton element whose <c>Content</c> is <paramref name="label"/>.</summary>
    private static string ToggleFor(string markup, string label)
    {
        var match = Regex.Match(
            markup,
            "<ToggleButton\\s+Content=\"" + Regex.Escape(label) + "\"[^>]*?/>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        match.Success.Should().BeTrue("the rail must have a ToggleButton for {0}", label);

        return match.Value;
    }

    private static string Read(params string[] relativeSegments)
    {
        var path = Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", Path.Combine(relativeSegments));

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
            ?? throw new InvalidOperationException($"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
