using Avalonia.Automation;
using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.8. Markup and one small piece of behaviour the P3-T12 <c>LabeledField</c> family cannot
/// express itself: the return-policy paragraph and the bill layout template both need a taller,
/// multi-line (<see cref="TextBox.AcceptsReturn"/>) box - the template box also needs a
/// fixed-width font and no wrapping - which the family does not offer, so both stay a plain
/// <see cref="TextBox"/>/<see cref="TextBlock"/> pair wired to
/// <c>AutomationProperties.LabeledBy</c> the same way <see cref="Catalogue.ImportTabView"/> and
/// <see cref="Catalogue.ProductTabView"/> wire a field the family cannot express (SRS UI-14).
/// Everything else on this view is a binding to
/// <see cref="ViewModels.Settings.ReceiptSettingsViewModel"/>.
/// </summary>
public partial class ReceiptSettingsView : UserControl
{
    public ReceiptSettingsView()
    {
        InitializeComponent();

        Wire("PolicyTextBox", "PolicyTextLabel");
        Wire("TemplateTextBox", "TemplateTextLabel");
    }

    private void Wire(string boxName, string labelName)
    {
        var box = this.FindControl<TextBox>(boxName);
        var label = this.FindControl<TextBlock>(labelName);

        if (box is not null && label is not null)
        {
            AutomationProperties.SetLabeledBy(box, label);
        }
    }
}
