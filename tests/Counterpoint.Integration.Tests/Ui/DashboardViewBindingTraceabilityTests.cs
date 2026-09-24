using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Dashboard;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// Task P3-T20's own "Done when" #4 (SRS FR-9.7, UI-16): "no tile renders a figure that does not
/// trace to an existing or P3-T22 query, verified by review: every binding in the view is
/// traceable to a named service method." A file-inspection test in the style of
/// <c>BackOfficeShellTests</c> (which already proves things about these same two files without
/// opening a window - a window cannot be opened in CI) rather than a headless Avalonia render:
/// this check is about where a binding's value comes from, not what renders on screen, so
/// <c>Counterpoint.Ui.Tests</c>' <c>ViewLabelInspector</c> pattern (visible-label association) does
/// not fit it.
/// </summary>
public sealed class DashboardViewBindingTraceabilityTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    private static readonly Regex BindingExpression = new(
        @"\{Binding\s+([^},]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Every <c>{Binding ...}</c> in <c>DashboardView.axaml</c> must resolve to a real member of
    /// <see cref="DashboardViewModel"/> itself, or - for the two <c>DataTemplate</c> item types the
    /// reorder-alerts and recent-sales <c>ItemsControl</c>s use - <see cref="ReorderAlertRow"/> or
    /// <see cref="RecentSaleRow"/>. Every one of those three types is itself nothing but a
    /// formatting pass over <see cref="IDashboardQueries"/>/<see cref="IReorderListQuery"/>/
    /// <see cref="IRecentSalesQuery"/> (see <see cref="DashboardViewModel"/>'s own remarks) - so a
    /// binding that resolves here traces all the way back to a named query, and a binding that
    /// does not resolve here would be exactly the invented-figure risk this task names.
    /// </summary>
    [Fact]
    public void UI_16_EveryBindingInDashboardViewResolvesToARealDashboardViewModelOrRowMember()
    {
        var markup = ReadUiFile("Views", "Dashboard", "DashboardView.axaml");

        var resolvable = new HashSet<string>(StringComparer.Ordinal);
        resolvable.UnionWith(MemberNames(typeof(DashboardViewModel)));
        resolvable.UnionWith(MemberNames(typeof(ReorderAlertRow)));
        resolvable.UnionWith(MemberNames(typeof(RecentSaleRow)));

        var unresolved = new List<string>();

        foreach (Match match in BindingExpression.Matches(markup))
        {
            var path = match.Groups[1].Value.Trim();

            // Strip a leading boolean negation ("!HasReorderAlerts") - Avalonia's own binding
            // parser accepts this against the same property the un-negated form would.
            if (path.StartsWith('!'))
            {
                path = path[1..];
            }

            // This view uses no nested/dotted binding paths and no named bindings (x:Name,
            // ElementName) - a bare property name every time - but take only the first segment
            // defensively, the same way a real binding resolves its own first hop.
            var propertyName = path.Split('.')[0].Trim();

            if (!resolvable.Contains(propertyName))
            {
                unresolved.Add(propertyName + " (from \"" + match.Value + "\")");
            }
        }

        unresolved.Should().BeEmpty(
            "every {Binding ...} in DashboardView.axaml must resolve to a real DashboardViewModel/"
            + "ReorderAlertRow/RecentSaleRow member, each backed by IDashboardQueries/"
            + "IReorderListQuery/IRecentSalesQuery - never a figure invented or hard-coded by the "
            + "view itself (task P3-T20's own named risk). Unresolved: " + string.Join("; ", unresolved));
    }

    /// <summary>
    /// The low-stock KPI card's own danger tint (task P3-T20 "Do this" #5) must be a dynamic
    /// <c>Classes</c> binding straight off <see cref="DashboardViewModel.HasLowStockAlerts"/>, not
    /// a converter or a raw threshold re-implemented in the view - the same figure
    /// <see cref="DashboardViewModel.LowStockCountText"/> itself already comes from, so the two
    /// can never disagree.
    /// </summary>
    [Fact]
    public void UI_16_TheLowStockKpiCardsDangerTintBindsDirectlyToHasLowStockAlerts()
    {
        var markup = ReadUiFile("Views", "Dashboard", "DashboardView.axaml");

        markup.Should().Contain("Classes.Danger=\"{Binding HasLowStockAlerts}\"");
    }

    /// <summary>
    /// The Overview quick actions and the <see cref="BackOfficeShellViewModel.CanOpenShift"/>
    /// visibility flag this task adds live on <c>BackOfficeShellWindow.axaml</c> itself, not on
    /// <c>DashboardView.axaml</c> (see that file's own header comment: "New sale"/"Open shift" are
    /// shell-level navigation, not a figure <see cref="DashboardViewModel"/> owns). Asserted by
    /// exact string match rather than the same path-resolution scan as the test above, since that
    /// window's markup carries bindings against <see cref="BackOfficeShellViewModel"/>,
    /// <c>CatalogueViewModel</c> and its own status bar all together - not worth disambiguating
    /// three view-models' worth of bindings for the four this task actually added.
    /// </summary>
    [Fact]
    public void UI_16_TheOverviewQuickActionsInBackOfficeShellWindowResolveToRealBackOfficeShellViewModelMembers()
    {
        var markup = ReadUiFile("Views", "BackOfficeShellWindow.axaml");

        markup.Should().Contain("Command=\"{Binding ReturnToSalesCommand}\"");
        markup.Should().Contain("IsVisible=\"{Binding CanOpenShift}\"");
        markup.Should().Contain("Command=\"{Binding Dashboard.LoadCommand}\"");

        var shellMembers = MemberNames(typeof(BackOfficeShellViewModel));
        shellMembers.Should().Contain("ReturnToSalesCommand");
        shellMembers.Should().Contain("CanOpenShift");
        shellMembers.Should().Contain("Dashboard");

        MemberNames(typeof(DashboardViewModel)).Should().Contain("LoadCommand");
    }

    private static HashSet<string> MemberNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            names.Add(property.Name);
        }

        return names;
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
