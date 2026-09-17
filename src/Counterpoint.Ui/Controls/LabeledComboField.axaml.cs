using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;

namespace Counterpoint.Ui.Controls;

/// <summary>
/// The combo/picker variant of the P3-T12 field family (SRS UI-06, UI-14, AC-22): a persistent
/// <see cref="Label"/> above a <see cref="ComboBox"/>, replacing the bare
/// <c>&lt;TextBlock&gt;</c>+<c>&lt;ComboBox&gt;</c> pair the Category screen used before task
/// P3-T11's dialog and this task's control both landed.
/// </summary>
public partial class LabeledComboField : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<LabeledComboField, string?>(nameof(Label));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<LabeledComboField, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<LabeledComboField, object?>(
            nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> ValidationMessageProperty =
        AvaloniaProperty.Register<LabeledComboField, string?>(nameof(ValidationMessage));

    public LabeledComboField()
    {
        InitializeComponent();

        var input = this.FindControl<ComboBox>("InputCombo");
        var label = this.FindControl<TextBlock>("LabelBlock");

        if (input is not null && label is not null)
        {
            AutomationProperties.SetLabeledBy(input, label);
        }
    }

    /// <summary>The persistent caption identifying what this field captures (SRS UI-14).</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>An inline validation message shown under the field, or null/empty for none.</summary>
    public string? ValidationMessage
    {
        get => GetValue(ValidationMessageProperty);
        set => SetValue(ValidationMessageProperty, value);
    }
}
