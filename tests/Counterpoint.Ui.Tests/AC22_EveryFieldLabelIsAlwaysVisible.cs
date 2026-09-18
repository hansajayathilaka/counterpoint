using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Counterpoint.Ui.Tests.Support;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// <b>AC-22</b> — "Every labelled input field's label remains visible before, during and after
/// the field contains a value, across every screen" (SRS UI-14). Task P3-T16's closing gate for
/// the whole UI redesign.
/// </summary>
/// <remarks>
/// <para>
/// Every earlier task in this series (<c>ViewLabelInspectorTests</c>, task P3-T12;
/// <c>CatalogueTabViewLabelInspectorTests</c>/<c>SettingsTabViewLabelInspectorTests</c>, task
/// P3-T15) proved this one screen at a time as each was converted. This test does not repeat
/// those - it reuses the exact same <see cref="ViewLabelInspector"/> assertion, but walks every
/// <see cref="Control"/> under <c>Counterpoint.Ui.Views</c> by reflection rather than a
/// hand-maintained list, so a screen added after this task still has to pass it. Nothing here is
/// hardcoded to a screen name.
/// </para>
/// <para>
/// Each discovered view is constructed with no <c>DataContext</c>, exactly as
/// <c>ViewLabelInspectorTests</c> already does for <c>CategoryEditView</c> and the rest: a field's
/// label is markup, fixed at construction, not something a bound value could ever cause to
/// appear or disappear - so the absence of a live viewmodel does not weaken what is being proved.
/// A row inside an <c>ItemsControl</c> bound to an empty design-time collection is not walked (no
/// row exists to walk without data) - that per-row shape is exactly what
/// <c>CatalogueTabViewLabelInspectorTests</c>/<c>ImportTabView</c>'s own dedicated tests already
/// cover for the templates that need it.
/// </para>
/// </remarks>
public sealed class AC22_EveryFieldLabelIsAlwaysVisible
{
    [AvaloniaFact]
    public void AC_22_EveryInspectableInputAcrossEveryScreenInTheRepositoryHasAVisibleLabel()
    {
        var violations = new List<string>();

        foreach (var type in DiscoverViewTypes())
        {
            var instance = (Control)Activator.CreateInstance(type)!;

            var found = instance is Window window
                ? ViewLabelInspector.FindInputsWithoutVisibleLabel(window)
                : ViewLabelInspector.FindInputsWithoutVisibleLabel(instance);

            violations.AddRange(found.Select(violation => type.FullName + ": " + violation));
        }

        violations.Should().BeEmpty(
            "SRS AC-22: every labelled input's label must remain visible before, during and after "
            + "the field contains a value, across every screen under Counterpoint.Ui.Views. "
            + "Violations: " + string.Join("; ", violations));
    }

    /// <summary>
    /// Every concrete, publicly constructible <see cref="Control"/> (a <see cref="Window"/> or a
    /// <see cref="UserControl"/>) under the <c>Counterpoint.Ui.Views</c> namespace - the same
    /// reflection-based discovery a future screen cannot opt out of by omission.
    /// </summary>
    private static List<Type> DiscoverViewTypes()
    {
        var assembly = typeof(Counterpoint.Ui.Views.LoginWindow).Assembly;

        return assembly.GetTypes()
            .Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.Namespace is { } ns
                && (ns == "Counterpoint.Ui.Views" || ns.StartsWith("Counterpoint.Ui.Views.", StringComparison.Ordinal))
                && typeof(Control).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }
}
