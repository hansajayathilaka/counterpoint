using Avalonia.Automation;
using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The sign-in screen. Every behaviour it has is a binding to
/// <see cref="ViewModels.LoginViewModel"/>, except one piece of task P3-T16 wiring (SRS UI-14,
/// AC-22): the two <c>PasswordChar</c>-masked boxes cannot be a P3-T12 <c>LabeledTextField</c>
/// (the family does not offer masking), so their labels are wired to
/// <c>AutomationProperties.LabeledBy</c> here, the same accepted fallback
/// <see cref="Settings.BackupSettingsView"/>'s own passphrase boxes use.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();

        Wire("PasswordBox", "PasswordLabel");
        Wire("ConfirmPasswordBox", "ConfirmPasswordLabel");
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
