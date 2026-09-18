using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Counterpoint.Ui.ViewModels.Settings;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// The guided restore wizard (SRS FR-11.12). Markup and one behaviour that cannot be a binding:
/// choosing a file needs <see cref="TopLevel.StorageProvider"/>, the same reasoning
/// <c>ImportTabView</c> gives for its own browse button. Task P3-T16 (SRS UI-14, AC-22): the
/// passphrase box's label is wired here the same way <see cref="BackupSettingsView"/>'s own two
/// passphrase boxes are.
/// </summary>
public partial class RestoreWizardWindow : Window
{
    public RestoreWizardWindow()
    {
        InitializeComponent();

        var passphraseBox = this.FindControl<TextBox>("PassphraseBox");
        var passphraseLabel = this.FindControl<TextBlock>("PassphraseLabel");

        if (passphraseBox is not null && passphraseLabel is not null)
        {
            AutomationProperties.SetLabeledBy(passphraseBox, passphraseLabel);
        }
    }

    private RestoreWizardViewModel? ViewModel => DataContext as RestoreWizardViewModel;

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Counterpoint backup",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Counterpoint backup (*.cpbk)") { Patterns = ["*.cpbk"] },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        viewModel.Reset();
        viewModel.FilePath = files[0].Path.LocalPath;
    }
}
