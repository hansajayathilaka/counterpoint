using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The owner's user-management screen. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.UserAdminViewModel"/>.
/// </summary>
public partial class UserAdminWindow : Window
{
    public UserAdminWindow()
    {
        InitializeComponent();
    }
}
