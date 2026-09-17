using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;

namespace Counterpoint.Ui.Controls;

/// <summary>
/// The numeric-entry variant of the P3-T12 field family (SRS UI-06, UI-14, AC-22): a persistent
/// <see cref="Label"/> above a right-aligned box, for binding to one of this codebase's
/// <see cref="Counterpoint.Ui.ViewModels.NumericInputViewModel.SetNumeric"/>-backed string
/// properties (e.g. a credit-limit or quantity field). The label behaves identically to
/// <see cref="LabeledTextField"/> in every state - empty, focused, filled, disabled.
/// </summary>
public partial class LabeledNumericField : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<LabeledNumericField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LabeledNumericField, string?>(
            nameof(Text),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<LabeledNumericField, string?>(nameof(Watermark));

    public static readonly StyledProperty<string?> ValidationMessageProperty =
        AvaloniaProperty.Register<LabeledNumericField, string?>(nameof(ValidationMessage));

    public LabeledNumericField()
    {
        InitializeComponent();

        var input = this.FindControl<TextBox>("InputBox");
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

    /// <summary>
    /// The sanitised numeric text, normally bound straight to a
    /// <see cref="Counterpoint.Ui.ViewModels.NumericInputViewModel"/>-derived property.
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Supplementary example text shown before typing - never the field's only label.</summary>
    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>An inline validation message shown under the field, or null/empty for none.</summary>
    public string? ValidationMessage
    {
        get => GetValue(ValidationMessageProperty);
        set => SetValue(ValidationMessageProperty, value);
    }
}
