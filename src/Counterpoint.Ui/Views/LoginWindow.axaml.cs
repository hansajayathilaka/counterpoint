using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The sign-in screen. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.LoginViewModel"/>.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }
}
