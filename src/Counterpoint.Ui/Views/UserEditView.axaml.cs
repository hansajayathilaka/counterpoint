using Avalonia.Automation;
using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The user create/reset-password dialog content. Markup and one piece of task P3-T16 wiring
/// (SRS UI-14, AC-22): the password box's label, wired here because it needs
/// <see cref="TextBox.PasswordChar"/> masking, which the P3-T12 <c>LabeledField</c> family does
/// not offer - the same accepted fallback <see cref="Settings.BackupSettingsView"/>'s own
/// passphrase boxes use. Everything else is a binding to
/// <see cref="ViewModels.UserEditViewModel"/>.
/// </summary>
public partial class UserEditView : UserControl
{
    public UserEditView()
    {
        InitializeComponent();

        var box = this.FindControl<TextBox>("PasswordBox");
        var label = this.FindControl<TextBlock>("PasswordLabel");

        if (box is not null && label is not null)
        {
            AutomationProperties.SetLabeledBy(box, label);
        }
    }
}
