using Avalonia.Automation;
using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.7. Markup and one small piece of behaviour the P3-T12 <c>LabeledField</c> family cannot
/// express itself: the credential box and the two passphrase boxes need
/// <see cref="TextBox.PasswordChar"/> masking, which the family does not offer, so they stay a
/// plain <see cref="TextBox"/>/<see cref="TextBlock"/> pair wired to
/// <c>AutomationProperties.LabeledBy</c> the same way
/// <see cref="Catalogue.ImportTabView"/> and <see cref="Catalogue.ProductTabView"/> wire a field
/// the family cannot express (SRS UI-14). Everything else on this view is a binding to
/// <see cref="ViewModels.Settings.BackupSettingsViewModel"/>.
/// </summary>
public partial class BackupSettingsView : UserControl
{
    public BackupSettingsView()
    {
        InitializeComponent();

        Wire("NewCredentialBox", "NewCredentialLabel");
        Wire("NewPassphraseBox", "NewPassphraseLabel");
        Wire("ConfirmPassphraseBox", "ConfirmPassphraseLabel");
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
