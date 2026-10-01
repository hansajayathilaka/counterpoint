using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Ui;

/// <summary>
/// Task P3-T06's file-level proofs for the eleven Stock, Tax and Cash screens (SRS UI-11, UI-13, UI-14, AC-17, AC-21):
/// the rail offers two new groups - stock on hand and the reorder list to any signed-in user, every other report to the
/// owner only - the section host mounts all eleven screens, every new markup file (the shared table and
/// <c>ReportStyles.axaml</c> included) is inside the repository-wide hex sweep and writes no raw colour, and
/// <c>Counterpoint.Ui</c> takes only the logging abstractions (ADR-0010). The same file-inspection style
/// <see cref="ReportsNavigationTests"/> uses, since a window cannot be opened here.
/// </summary>
public sealed class StockCashReportsNavigationTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    private static readonly string[] NewScreenFiles =
    [
        "StockOnHandView", "ReorderListView", "StockValuationView", "StockCardView", "SlowMovingStockView", "FastMovingView",
        "DamageAdjustmentView", "SupplierPurchasesView", "TaxReportView", "TenderReconciliationView", "ShiftVarianceView",
    ];

    private static readonly string[] SharedFiles = ["ReportTableView.axaml", "ReportStyles.axaml"];

    private static readonly string[] CashierSections = ["Stock on hand", "Reorder list"];

    private static readonly string[] OwnerStockSections =
    [
        "Stock valuation", "Stock card", "Slow-moving stock", "Fast-moving items", "Damage and adjustments", "Supplier purchases",
    ];

    private static readonly string[] OwnerCashSections = ["Tax", "Tender reconciliation", "Shift variance"];

    private static readonly Regex HexColourLiteral = new(
        @"#[0-9A-Fa-f]{3,8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void UI_13_EveryNewReportMarkupFileExistsIsInsideTheRepositoryWideHexSweepAndWritesNoRawColour()
    {
        var swept = NoRawHexColourLiteralsTests.AllViewAxamlFiles().Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        var expected = NewScreenFiles.Select(name => name + ".axaml").Concat(SharedFiles).ToList();

        foreach (var name in expected)
        {
            File.Exists(Path.Combine(ViewsDirectory(), "Reports", name)).Should().BeTrue("{0} is part of this task", name);
            swept.Should().Contain(name, "the recursive sweep must pick up {0}", name);

            var file = new FileInfo(Path.Combine(ViewsDirectory(), "Reports", name));
            NoRawHexColourLiteralsTests.FindHexOffences(file).Should().BeEmpty("{0} must use a semantic DynamicResource token, never a hex literal (SRS UI-13, NFR-U4)", name);
        }

        // The sweep itself also covers the rail and the section host, which this task edited.
        swept.Should().Contain(["NavRail.axaml", "ReportSectionContent.axaml"]);
        NoRawHexColourLiteralsTests.FindHexOffences(new FileInfo(Path.Combine(ViewsDirectory(), "NavRail.axaml"))).Should().BeEmpty();
        NoRawHexColourLiteralsTests.FindHexOffences(new FileInfo(Path.Combine(ViewsDirectory(), "Reports", "ReportSectionContent.axaml"))).Should().BeEmpty();
    }

    [Fact]
    public void UI_13_ReportStylesNamesOnlySemanticTokensAndEveryOneIsDefinedInBothThemes()
    {
        var styles = Read("Views", "Reports", "ReportStyles.axaml");
        var used = Regex.Matches(styles, @"DynamicResource\s+(\w+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        used.Should().NotBeEmpty();
        HexColourLiteral.IsMatch(styles).Should().BeFalse();

        var light = Read("Styles", "Tokens.Light.axaml");
        var dark = Read("Styles", "Tokens.Dark.axaml");

        foreach (var token in used)
        {
            light.Should().Contain("x:Key=\"" + token + "\"", "{0} must exist in the light theme", token);
            dark.Should().Contain("x:Key=\"" + token + "\"", "{0} must exist in the dark theme", token);
        }
    }

    [Fact]
    public void UI_14_EveryNewScreenAndTheSharedTableDeclareACompiledBindingDataType()
    {
        foreach (var name in NewScreenFiles.Select(file => file + ".axaml").Append("ReportTableView.axaml"))
        {
            Read("Views", "Reports", name).Should().Contain("x:DataType=", "{0} must use compiled bindings", name);
        }
    }

    [Fact]
    public void UI_11_TheRailHoldsTheStockReportsGroupForAnySignedInUserAndTheTaxAndCashGroupForTheOwnerOnly()
    {
        var markup = Read("Views", "NavRail.axaml");

        var stockGroup = GroupOf(markup, "STOCK REPORTS");
        var cashGroup = GroupOf(markup, "TAX AND CASH REPORTS");

        stockGroup.Should().Contain("IsVisible=\"{Binding CanViewReports}\"", "any signed-in user sees the group");
        cashGroup.Should().Contain("IsVisible=\"{Binding CanViewOwnerReports}\"", "the whole tax and cash group is owner-only");

        foreach (var section in CashierSections)
        {
            ToggleFor(stockGroup, section).Should().NotContain("IsVisible=", "{0} carries no cost figure and is open to both roles", section);
        }

        foreach (var section in OwnerStockSections)
        {
            ToggleFor(stockGroup, section).Should().Contain("IsVisible=\"{Binding CanViewOwnerReports}\"", "{0} is owner-only", section);
        }

        foreach (var section in OwnerCashSections)
        {
            cashGroup.Should().Contain("CommandParameter=\"" + section + "\"", "the rail must offer {0}", section);
        }

        foreach (var section in CashierSections.Concat(OwnerStockSections).Concat(OwnerCashSections))
        {
            markup.Should().Contain("CommandParameter=\"" + section + "\"");
            markup.Should().Contain("Command=\"{Binding SelectReportSectionCommand}\" CommandParameter=\"" + section + "\"");
        }
    }

    [Fact]
    public void UI_11_TheSectionHostMountsEveryNewScreenOnItsOwnSectionNameAndNothingElseIsHandWiredInTheShell()
    {
        var host = Read("Views", "Reports", "ReportSectionContent.axaml");

        var expectedPairs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StockOnHandView"] = "Stock on hand",
            ["ReorderListView"] = "Reorder list",
            ["StockValuationView"] = "Stock valuation",
            ["StockCardView"] = "Stock card",
            ["SlowMovingStockView"] = "Slow-moving stock",
            ["FastMovingView"] = "Fast-moving items",
            ["DamageAdjustmentView"] = "Damage and adjustments",
            ["SupplierPurchasesView"] = "Supplier purchases",
            ["TaxReportView"] = "Tax",
            ["TenderReconciliationView"] = "Tender reconciliation",
            ["ShiftVarianceView"] = "Shift variance",
        };

        foreach (var (view, section) in expectedPairs)
        {
            var element = Regex.Match(
                host,
                "<views:" + view + @"\s[^>]*?>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));

            element.Success.Should().BeTrue("the host must mount {0}", view);
            element.Value.Should().Contain("ConverterParameter='" + section + "'", "{0} shows only for '{1}'", view, section);
            element.Value.Should().Contain("#Root.Reports.StockAndCash.");
        }

        expectedPairs.Should().HaveCount(NewScreenFiles.Length);
    }

    [Fact]
    public void UI_11_EveryNewSectionNameIsOneTheShellListsSoTheRailAndTheHostCannotDrift()
    {
        var shell = Read("ViewModels", "BackOfficeShellViewModel.cs");
        var container = Read("ViewModels", "Reports", "StockAndCashReportsViewModel.cs");

        foreach (var constant in new[]
        {
            "StockOnHandSection", "ReorderSection", "StockValuationSection", "StockCardSection", "SlowMovingSection",
            "FastMovingSection", "DamageSection", "SupplierPurchasesSection", "TaxSection", "TenderReconciliationSection", "ShiftVarianceSection",
        })
        {
            shell.Should().Contain("StockAndCashReportsViewModel." + constant, "the shell's ReportSectionNames lists it");
            container.Should().Contain("const string " + constant, "the container defines it");
        }
    }

    [Fact]
    public void ADR_0010_CounterpointUiTakesTheLoggingAbstractionsAndNoLoggerImplementationOrHost()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Counterpoint.Ui.csproj"));
        var packages = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToList();

        packages.Should().Contain("Microsoft.Extensions.Logging.Abstractions", "a report screen logs a failed read (ADR-0010)");
        packages.Should().NotContain(name => name.StartsWith("Serilog", StringComparison.Ordinal), "the sink is the composition root's choice");
        packages.Should().NotContain("Microsoft.Extensions.Logging", "abstractions only - never the implementation");
        packages.Should().NotContain(name => name.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal));
        packages.Should().NotContain(name => name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) || name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase) || name.Contains("Dapper", StringComparison.OrdinalIgnoreCase));

        // And the screens take ILogger<T>? optionally, so a test or a host without logging can still build one.
        Read("ViewModels", "Reports", "ReportScreenViewModelBase.cs").Should().Contain("ILogger? logger").And.Contain("NullLogger.Instance");
    }

    private static string GroupOf(string markup, string heading)
    {
        var start = markup.IndexOf("Text=\"" + heading + "\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the rail must have the {0} heading", heading);

        var open = markup.LastIndexOf("<StackPanel", start, StringComparison.Ordinal);
        var close = markup.IndexOf("</StackPanel>", start, StringComparison.Ordinal);

        return markup[open..(close + "</StackPanel>".Length)];
    }

    private static string ToggleFor(string group, string label)
    {
        var match = Regex.Match(
            group,
            "<ToggleButton\\s+Content=\"" + Regex.Escape(label) + "\"[^>]*?>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        match.Success.Should().BeTrue("the group must have a ToggleButton for {0}", label);

        return match.Value;
    }

    private static string ViewsDirectory() => Path.Combine(RepositoryRoot().FullName, "src", "Counterpoint.Ui", "Views");

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
