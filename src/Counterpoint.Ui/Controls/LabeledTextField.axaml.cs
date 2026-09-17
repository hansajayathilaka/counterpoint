using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;

namespace Counterpoint.Ui.Controls;

/// <summary>
/// A text field that always shows what it captures (SRS UI-06, UI-14, AC-22): a persistent
/// <see cref="Label"/> above the box, which stays put whether the box is empty, focused, filled
/// or disabled - unlike the old pattern of using <see cref="TextBox.Watermark"/> as the only
/// label, which disappears the moment a value is typed (the confirmed "cannot see the label once
/// filled" defect task P3-T12 exists to close).
/// </summary>
/// <remarks>
/// <see cref="Watermark"/> is retained only as supplementary example text shown inside the box
/// before typing (e.g. "e.g. 07X XXX XXXX") - it is never the field's only label. The constructor
/// wires <c>AutomationProperties.LabeledBy</c> from the input to the label TextBlock, which is
/// exactly what task P3-T12's view-inspection test helper
/// (<c>Counterpoint.Ui.Tests.Support.ViewLabelInspector</c>) checks for.
/// </remarks>
public partial class LabeledTextField : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<LabeledTextField, string?>(nameof(Label));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LabeledTextField, string?>(
            nameof(Text),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<LabeledTextField, string?>(nameof(Watermark));

    public static readonly StyledProperty<string?> ValidationMessageProperty =
        AvaloniaProperty.Register<LabeledTextField, string?>(nameof(ValidationMessage));

    public LabeledTextField()
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

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Supplementary example text shown before typing (SRS UI-14: "Placeholder/watermark text is
    /// not a substitute for a label"). Leave unset if there is nothing useful to show.
    /// </summary>
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
