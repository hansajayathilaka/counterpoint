using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Counterpoint.Ui.Controls;

/// <summary>
/// The checkbox variant of the P3-T12 field family (SRS UI-06, UI-14, AC-22). A
/// <see cref="CheckBox"/>'s own <see cref="CheckBox.Content"/> already is a persistent,
/// always-visible label next to the box - unlike a <see cref="TextBox"/> watermark it never
/// disappears - so this
/// control's job is consistency with the rest of the family (an optional group heading via
/// <see cref="Label"/>) rather than fixing a labelling defect the plain <c>CheckBox</c> did not
/// have.
/// </summary>
public partial class LabeledCheckBoxField : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<LabeledCheckBoxField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> CheckBoxLabelProperty =
        AvaloniaProperty.Register<LabeledCheckBoxField, string?>(nameof(CheckBoxLabel));

    public static readonly StyledProperty<bool?> IsCheckedProperty =
        AvaloniaProperty.Register<LabeledCheckBoxField, bool?>(
            nameof(IsChecked),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> ValidationMessageProperty =
        AvaloniaProperty.Register<LabeledCheckBoxField, string?>(nameof(ValidationMessage));

    public LabeledCheckBoxField()
    {
        InitializeComponent();
    }

    /// <summary>An optional group heading shown above the box; collapsed when unset.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The checkbox's own persistent label text, shown next to the box.</summary>
    public string? CheckBoxLabel
    {
        get => GetValue(CheckBoxLabelProperty);
        set => SetValue(CheckBoxLabelProperty, value);
    }

    public bool? IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>An inline validation message shown under the field, or null/empty for none.</summary>
    public string? ValidationMessage
    {
        get => GetValue(ValidationMessageProperty);
        set => SetValue(ValidationMessageProperty, value);
    }
}
