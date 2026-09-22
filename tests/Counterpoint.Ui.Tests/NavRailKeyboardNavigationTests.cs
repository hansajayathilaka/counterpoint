using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Counterpoint.Application.Security;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.Views;
using Counterpoint.Ui.Views.Catalogue;
using FluentAssertions;
using DomainRole = Counterpoint.Domain.Security.Role;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T18's own named risk (SRS UI-01): folding <c>CatalogueWindow</c>'s
/// <see cref="TabControl"/> into the back-office shell's nav rail must not lose the
/// <see cref="TabControl"/>'s built-in keyboard selection - arrowing between destinations has to
/// actually swap the content pane, not merely compile. Proven here with a real (headless) key
/// press dispatched through <see cref="HeadlessWindowExtensions.KeyPressQwerty"/>, the same routed
/// path a real keyboard uses, exactly as <c>LabeledFieldKeyBindingTests</c> (task P3-T14) already
/// proves its own keyboard-binding risk this same way.
/// </summary>
public sealed class NavRailKeyboardNavigationTests
{
    [AvaloniaFact]
    public void UI_01_ArrowingThroughTheCatalogueListBoxSelectsASectionWithNoMouse()
    {
        var shell = new BackOfficeShellViewModel(new OwnerSession());
        var rail = new NavRail { DataContext = shell };

        var window = new Window { Content = rail };
        window.Show();

        var catalogueList = rail.GetVisualDescendants().OfType<ListBox>().Single();
        var focused = catalogueList.Focus();

        focused.Should().BeTrue("the Catalogue ListBox must be reachable by keyboard focus alone");

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        shell.SelectedCatalogueSection.Should().Be(
            BackOfficeShellViewModel.CatalogueSectionNames[0],
            "the first Down-arrow press inside the Catalogue ListBox must select its first item, "
                + "immediately changing the content pane - exactly the TabControl behaviour this "
                + "task's nav rail replaces");

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        shell.SelectedCatalogueSection.Should().Be(
            BackOfficeShellViewModel.CatalogueSectionNames[1],
            "a second Down-arrow press must move to the next Catalogue destination, with no mouse "
                + "and no extra Enter/activation keystroke - the same one-keystroke-per-item "
                + "behaviour a TabControl's own tab strip already gave every other screen");
    }

    /// <summary>
    /// The gap the test above leaves open: it proves the key press changes
    /// <see cref="BackOfficeShellViewModel.SelectedCatalogueSection"/>, a viewmodel property, not
    /// that the shell's own content pane actually swapped - the specific claim this task's "Done
    /// when" #3 and this file's own doc comment make ("has to actually swap the content pane, not
    /// merely compile"). Proven here against the real <see cref="BackOfficeShellWindow"/> markup -
    /// <c>NavRail</c> and <c>CatalogueSectionContent</c> wired exactly as
    /// <c>BackOfficeShellWindow.axaml</c> declares them, not each control built and bound by hand
    /// in isolation - so the same Down-arrow key press that moves the <see cref="ListBox"/>
    /// selection is also proven, in one test, to reach the visible <see cref="CategoryTabView"/>
    /// on the other side of the two bindings (<c>SelectedItem</c> then <c>SelectedSection</c>) in
    /// between.
    /// </summary>
    [AvaloniaFact]
    public void UI_01_ArrowingThroughTheCatalogueListBoxSwapsTheVisibleTabInTheRealShellWindow()
    {
        var shell = new BackOfficeShellViewModel(new OwnerSession());
        var window = new BackOfficeShellWindow { DataContext = shell };
        window.Show();

        var catalogueList = window.GetVisualDescendants().OfType<ListBox>().Single();
        var focused = catalogueList.Focus();

        focused.Should().BeTrue("the Catalogue ListBox must be reachable by keyboard focus alone");

        var content = window.GetVisualDescendants().OfType<CatalogueSectionContent>().Single();

        content.IsVisible.Should().BeFalse(
            "before any key is pressed the pane still shows Overview, exactly as it did before "
                + "task P3-T18");

        // CatalogueSectionContent's own eight tab views are not instantiated in the visual tree
        // at all until the control that hosts them is first shown (Avalonia does not apply a
        // Control's template while IsVisible is false) - so they can only be looked up after the
        // first selection makes the pane visible, not before it.
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        content.IsVisible.Should().BeTrue(
            "one Down-arrow press inside the Catalogue ListBox must be enough, with no mouse and "
                + "no separate activation keystroke, to swap the shell's content pane away from "
                + "Overview - the TabControl behaviour this rail replaces");

        var categoryTab = window.GetVisualDescendants().OfType<CategoryTabView>().Single();
        var brandTab = window.GetVisualDescendants().OfType<BrandTabView>().Single();

        categoryTab.IsVisible.Should().BeTrue(
            "the first Catalogue destination, Categories, must be the one showing - CategoryTabView "
                + "specifically, not merely 'some' tab");
        brandTab.IsVisible.Should().BeFalse(
            "only the selected section's own tab may be visible; every other tab underneath the "
                + "same content pane must stay hidden");

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        categoryTab.IsVisible.Should().BeFalse(
            "a second Down-arrow press must move the visible tab on too, not merely move the "
                + "ListBox's own highlighted row");
        brandTab.IsVisible.Should().BeTrue(
            "the second Catalogue destination, Brands, must now be the one showing");
    }

    [AvaloniaFact]
    public void UI_01_SelectingOverviewAfterCatalogueClearsTheCatalogueSelection()
    {
        var shell = new BackOfficeShellViewModel(new OwnerSession())
        {
            SelectedCatalogueSection = BackOfficeShellViewModel.CatalogueSectionNames[2],
        };

        shell.IsCatalogueSectionActive.Should().BeTrue();

        shell.SelectOverviewCommand.Execute(null);

        shell.SelectedCatalogueSection.Should().BeNull(
            "the Overview nav item must be reachable with no mouse (its own Command) and must "
                + "return the content pane to Overview, clearing whichever Catalogue section was "
                + "showing");
        shell.IsCatalogueSectionActive.Should().BeFalse();
    }

    private sealed class OwnerSession : ISession
    {
        public AuthenticatedUser? CurrentUser { get; } = new(1, "owner", "Owner", DomainRole.Owner);

        public bool IsAuthenticated => true;

        public DomainRole? Role => DomainRole.Owner;

        public long? ShiftId => null;
    }
}
