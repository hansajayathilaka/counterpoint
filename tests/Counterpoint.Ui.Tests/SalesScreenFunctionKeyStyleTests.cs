using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Counterpoint.Ui.Views;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T21's second "Done when" line (SRS UI-02, UI-03, NFR-U4): "F9 Pay is visually the most
/// prominent function key (size/weight/colour); all thirteen keys remain present and bound to
/// their existing Gestures." The task doc's own contrast test
/// (<c>Counterpoint.Domain.Tests.Ui.SalesScreenContrastTests</c>) proves F9's colours are legible;
/// it does not prove F9 actually stands out from its twelve siblings. This test closes that gap
/// by opening the real (headless) <see cref="SalesWindow"/> - the same technique
/// <c>ViewLabelInspector</c> and <c>DesignTokenResourceResolutionTests</c> already use in this
/// project - and comparing F9's resolved, style-driven <see cref="Button"/> properties against
/// every other key in the strip, rather than reading the XAML and trusting it by eye.
/// </summary>
/// <remarks>
/// Deliberately does not touch <c>Command</c>/<c>Gesture</c> bindings - those are proven unchanged
/// by diff review (this task's fourth "Done when" line) and by every existing P1-T09 test passing
/// unmodified. This test is purely about the thirteen buttons' resolved visual properties.
/// </remarks>
public sealed class SalesScreenFunctionKeyStyleTests
{
    [AvaloniaFact]
    public void UI_02_AllThirteenFunctionKeysArePresentInTheStrip()
    {
        var window = new SalesWindow();

        try
        {
            window.Show();

            var buttons = FunctionKeyButtons(window);

            buttons.Should().HaveCount(
                13,
                "SRS UI-02 fixes the function-key strip at the twelve F-keys plus Escape, and "
                + "P3-T21's restyle must not add, remove or hide any of them");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UI_02_F9PayIsTheLargestFunctionKeyByFontSize()
    {
        var window = new SalesWindow();

        try
        {
            window.Show();

            var (f9, others) = F9AndItsSiblings(window);

            foreach (var other in others)
            {
                f9.FontSize.Should().BeGreaterThan(
                    other.FontSize,
                    "F9 Pay's font size ({0}) must exceed every other function key's ({1} on {2}) "
                    + "for F9 to actually read as the most prominent key, not merely be styled "
                    + "differently",
                    f9.FontSize,
                    other.FontSize,
                    Describe(other));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UI_02_F9PayIsTheOnlyBoldFunctionKey()
    {
        var window = new SalesWindow();

        try
        {
            window.Show();

            var (f9, others) = F9AndItsSiblings(window);

            f9.FontWeight.Should().Be(FontWeight.Bold, "F9 Pay must be visually weighted as the primary action");

            others.Should().OnlyContain(
                other => other.FontWeight != FontWeight.Bold,
                "if a sibling key is also bold, F9 no longer reads as uniquely dominant by weight");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UI_02_F9PayHasAFilledBackgroundNoOtherFunctionKeyShares()
    {
        var window = new SalesWindow();

        try
        {
            window.Show();

            var (f9, others) = F9AndItsSiblings(window);

            var f9Background = ResolvedColour(f9.Background);

            f9Background.Should().NotBeNull(
                "F9 Pay must resolve a concrete fill colour (RailActiveBackgroundBrush) once the window is shown, "
                + "not an unresolved or transparent background");

            foreach (var other in others)
            {
                var otherBackground = ResolvedColour(other.Background);

                otherBackground.Should().NotBe(
                    f9Background,
                    "{0}'s resolved background must not match F9 Pay's - a shared fill colour would mean F9 no "
                    + "longer stands out by colour alone",
                    Describe(other));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static (Button F9, List<Button> Others) F9AndItsSiblings(SalesWindow window)
    {
        var buttons = FunctionKeyButtons(window);

        var f9 = buttons.Single(button => Content(button) == "F9 Pay");
        var others = buttons.Where(button => !ReferenceEquals(button, f9)).ToList();

        return (f9, others);
    }

    /// <summary>
    /// The thirteen buttons inside the UniformGrid function-key strip, in document order - the
    /// window's other buttons (Back office, Print queue, Open item, Open shift, Dashboard) live
    /// outside that grid and are deliberately excluded.
    /// </summary>
    private static List<Button> FunctionKeyButtons(SalesWindow window)
    {
        var strip = window.GetVisualDescendants().OfType<UniformGrid>().Single();

        return strip.GetVisualDescendants().OfType<Button>().ToList();
    }

    private static string? Content(Button button) => button.Content as string;

    private static string Describe(Button button) => Content(button) ?? "(unnamed function key)";

    private static Color? ResolvedColour(IBrush? brush) => (brush as ISolidColorBrush)?.Color;
}
