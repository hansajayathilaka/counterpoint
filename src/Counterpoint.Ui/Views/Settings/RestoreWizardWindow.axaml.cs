using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Counterpoint.Ui.ViewModels.Settings;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// The guided restore wizard (SRS FR-11.12). Markup and one behaviour that cannot be a binding:
/// choosing a file needs <see cref="TopLevel.StorageProvider"/>, the same reasoning
/// <c>ImportTabView</c> gives for its own browse button.
/// </summary>
public partial class RestoreWizardWindow : Window
{
    public RestoreWizardWindow()
    {
        InitializeComponent();
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
