using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.Views.Catalogue;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T18's own "Done when" #2: selecting each of the eight Catalogue nav items must swap the
/// content pane to the correct existing tab view, and only that one. Proven here directly against
/// <see cref="CatalogueSectionContent"/>'s own <see cref="CatalogueSectionContent.SelectedSection"/>
/// property - the same string values <c>NavRail</c>'s <c>ListBox</c> and
/// <c>BackOfficeShellViewModel.CatalogueSectionNames</c> already share.
/// </summary>
public sealed class CatalogueSectionContentTests
{
    private static readonly Dictionary<string, string> ExpectedViewTypeNameBySection =
        new()
        {
            ["Categories"] = "CategoryTabView",
            ["Brands"] = "BrandTabView",
            ["Units"] = "UomTabView",
            ["Tax classes"] = "TaxClassTabView",
            ["Suppliers"] = "SupplierTabView",
            ["Customers"] = "CustomerTabView",
            ["Products"] = "ProductTabView",
            ["Import / Export"] = "ImportTabView",
        };

    [AvaloniaTheory]
    [InlineData("Categories")]
    [InlineData("Brands")]
    [InlineData("Units")]
    [InlineData("Tax classes")]
    [InlineData("Suppliers")]
    [InlineData("Customers")]
    [InlineData("Products")]
    [InlineData("Import / Export")]
    public void UI_16_SelectingASectionShowsExactlyThatTabAndHidesTheOtherSeven(string section)
    {
        BackOfficeShellViewModel.CatalogueSectionNames.Should().Contain(
            section, "the test's own fixture must stay in sync with the rail's real section list");

        var content = new CatalogueSectionContent { SelectedSection = section };
        var window = new Window { Content = content };
        window.Show();

        foreach (var tab in FindTabViews(content))
        {
            var expectedVisible = tab.GetType().Name == ExpectedViewTypeNameBySection[section];

            tab.IsVisible.Should().Be(
                expectedVisible,
                "selecting '{0}' must show only {1}, not {2}",
                section,
                ExpectedViewTypeNameBySection[section],
                tab.GetType().Name);
        }
    }

    [AvaloniaFact]
    public void UI_16_NoSectionSelectedShowsNothing()
    {
        var content = new CatalogueSectionContent();
        var window = new Window { Content = content };
        window.Show();

        foreach (var tab in FindTabViews(content))
        {
            tab.IsVisible.Should().BeFalse(
                "with no Catalogue section selected the content pane shows Overview instead, so "
                    + "every tab underneath must stay hidden");
        }
    }

    private static List<Control> FindTabViews(CatalogueSectionContent content)
    {
        var expectedNames = new HashSet<string>(ExpectedViewTypeNameBySection.Values);
        var found = new List<Control>();

        foreach (var visual in content.GetVisualDescendants())
        {
            if (visual is Control candidate && expectedNames.Contains(candidate.GetType().Name))
            {
                found.Add(candidate);
            }
        }

        found.Should().HaveCount(8, "all eight tab views must exist in the visual tree regardless of which one is visible");

        return found;
    }
}
