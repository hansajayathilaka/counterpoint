using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Counterpoint.Application.Security;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.Views;
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
