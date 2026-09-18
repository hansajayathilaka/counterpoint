using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Counterpoint.Ui.Controls;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T12's own done-when proof (SRS UI-06, UI-14, AC-22): every control in the
/// <c>LabeledField</c> family shows its persistent label whether the field is empty, focused,
/// filled or disabled - the exact four states the old watermark-only pattern could not survive
/// (a watermark disappears once filled, and says nothing about focus or disablement).
/// </summary>
public sealed class LabeledFieldStateTests
{
    [AvaloniaFact]
    public void UI_14_LabeledTextFieldShowsItsLabelWhenEmptyFocusedFilledAndDisabled()
    {
        var field = new LabeledTextField { Label = "Category name", Text = string.Empty };
        Show(field);

        var label = LabelOf(field);
        var input = field.GetVisualDescendants().OfType<TextBox>().Single();

        // Empty.
        AssertLabelVisible(label, "Category name");

        // Focused.
        input.Focus();
        AssertLabelVisible(label, "Category name");

        // Filled.
        field.Text = "Fasteners";
        AssertLabelVisible(label, "Category name");

        // Disabled.
        field.IsEnabled = false;
        AssertLabelVisible(label, "Category name");
    }

    [AvaloniaFact]
    public void UI_14_LabeledNumericFieldShowsItsLabelWhenEmptyFocusedFilledAndDisabled()
    {
        var field = new LabeledNumericField { Label = "Credit limit", Text = string.Empty };
        Show(field);

        var label = LabelOf(field);
        var input = field.GetVisualDescendants().OfType<TextBox>().Single();

        AssertLabelVisible(label, "Credit limit");

        input.Focus();
        AssertLabelVisible(label, "Credit limit");

        field.Text = "50000";
        AssertLabelVisible(label, "Credit limit");

        field.IsEnabled = false;
        AssertLabelVisible(label, "Credit limit");
    }

    [AvaloniaFact]
    public void UI_14_LabeledComboFieldShowsItsLabelWhenEmptyFocusedFilledAndDisabled()
    {
        var options = new[] { "Top level", "Fasteners" };
        var field = new LabeledComboField { Label = "Parent category", ItemsSource = options };
        Show(field);

        var label = LabelOf(field);
        var input = field.GetVisualDescendants().OfType<ComboBox>().Single();

        // Empty (no selection).
        AssertLabelVisible(label, "Parent category");

        input.Focus();
        AssertLabelVisible(label, "Parent category");

        field.SelectedItem = "Fasteners";
        AssertLabelVisible(label, "Parent category");

        field.IsEnabled = false;
        AssertLabelVisible(label, "Parent category");
    }

    [AvaloniaFact]
    public void UI_14_LabeledCheckBoxFieldShowsItsPersistentContentInEveryState()
    {
        var field = new LabeledCheckBoxField { CheckBoxLabel = "Trade customer", IsChecked = false };
        Show(field);

        var checkBox = field.GetVisualDescendants().OfType<CheckBox>().Single();

        // Unchecked ("empty").
        checkBox.Content.Should().Be("Trade customer");
        checkBox.IsVisible.Should().BeTrue();

        checkBox.Focus();
        checkBox.Content.Should().Be("Trade customer");

        // "Filled".
        field.IsChecked = true;
        checkBox.Content.Should().Be("Trade customer");

        field.IsEnabled = false;
        checkBox.Content.Should().Be("Trade customer");
        checkBox.IsVisible.Should().BeTrue();
    }

    private static void Show(Control content)
    {
        var window = new Window { Content = content };
        window.Show();
    }

    private static TextBlock LabelOf(Control field) =>
        field.GetVisualDescendants().OfType<TextBlock>().First();

    private static void AssertLabelVisible(TextBlock label, string expectedText)
    {
        label.IsVisible.Should().BeTrue();
        label.Text.Should().Be(expectedText);
    }
}
