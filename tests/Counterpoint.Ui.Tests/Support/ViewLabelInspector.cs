using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Counterpoint.Ui.Tests.Support;

/// <summary>
/// Task P3-T12's general view-inspection helper (SRS UI-06, UI-14, AC-22): hosts an arbitrary
/// view in a real (headless) window, forces layout, and reports every text/numeric/combo input
/// that has no associated, currently-visible label - the "watermark as the only label disappears
/// once you type" defect this task exists to close.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not hardcoded to one screen or to the <c>LabeledField</c> control family: it
/// checks Avalonia's own accessible-labelling API, <c>AutomationProperties.LabeledBy</c>
/// (<see cref="AutomationProperties.LabeledByProperty"/>), which the P3-T12 controls set on
/// their input from their label <see cref="TextBlock"/>. Any future control or hand-written view
/// that wires the same association passes; anything that only sets a
/// <see cref="TextBox.Watermark"/>, or nothing at all, is reported. Tasks P3-T15 and P3-T16 reuse
/// this exact type against every other catalogue/settings screen and window.
/// </para>
/// <para>
/// Must be called from an <c>[AvaloniaFact]</c>/<c>[AvaloniaTheory]</c> test method - it creates
/// real Avalonia objects, which is only legal on the dispatcher thread
/// <c>Avalonia.Headless.XUnit</c> already marshals those attributes onto.
/// </para>
/// </remarks>
public static class ViewLabelInspector
{
    /// <summary>
    /// Returns one description per <see cref="TextBox"/> or <see cref="ComboBox"/> descendant of
    /// <paramref name="view"/> that has no visible, non-blank label associated with it. An empty
    /// list means every inspectable input in the view is properly labelled.
    /// </summary>
    public static IReadOnlyList<string> FindInputsWithoutVisibleLabel(Control view)
    {
        ArgumentNullException.ThrowIfNull(view);

        // A plain, unowned top-level window - never SalesWindow/the real desktop lifetime - is
        // enough to force a real layout pass over the view's visual tree.
        var window = new Window { Content = view, Width = 1000, Height = 800 };

        try
        {
            window.Show();

            return Inspect(view);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The same inspection as <see cref="FindInputsWithoutVisibleLabel(Control)"/>, but for one of
    /// task P3-T16's remaining hand-built top-level <see cref="Window"/>s (Login, the first-run
    /// wizard, user admin, purchase orders, label printing, the print queue, guided restore),
    /// which were never split into a separate <see cref="UserControl"/> the way
    /// <c>SalesSidePanelView</c> was in task P3-T14. Shown directly rather than wrapped as another
    /// window's <see cref="ContentControl.Content"/> - a <see cref="Window"/> is its own top-level
    /// platform surface and cannot be nested inside one - but walks the exact same visual tree and
    /// applies the exact same rule.
    /// </summary>
    public static IReadOnlyList<string> FindInputsWithoutVisibleLabel(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            window.Show();

            return Inspect(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static List<string> Inspect(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsInspectableInput)
            .Where(input => !HasVisibleLabel(input))
            .Select(Describe)
            .ToList();

    private static bool IsInspectableInput(Control control) => control is TextBox or ComboBox;

    private static bool HasVisibleLabel(Control input)
    {
        var labelledBy = AutomationProperties.GetLabeledBy(input);

        return labelledBy is TextBlock { IsVisible: true } label
            && !string.IsNullOrWhiteSpace(label.Text);
    }

    private static string Describe(Control input)
    {
        var name = string.IsNullOrEmpty(input.Name) ? "(unnamed)" : input.Name;

        return $"{input.GetType().Name} '{name}' has no associated, currently-visible label "
            + "(AutomationProperties.LabeledBy must point at a visible, non-blank TextBlock)";
    }
}
