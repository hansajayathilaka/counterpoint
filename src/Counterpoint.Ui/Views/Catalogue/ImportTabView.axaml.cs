using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Counterpoint.Ui.ViewModels.Catalogue;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The import tab. Markup and one behaviour that cannot be a binding: choosing a file needs
/// <see cref="TopLevel.StorageProvider"/>, which only a control attached to a window can reach
/// (SRS FR-2.22, FR-2.23, docs/03_PHASE_1_core_trading.md P1-T13).
/// </summary>
public partial class ImportTabView : UserControl
{
    public ImportTabView()
    {
        InitializeComponent();

        // The "Saved profile" combo keeps its own ItemTemplate (rendering ImportMappingProfile.Name),
        // so it stays a plain ComboBox rather than the P3-T12 LabeledComboField (which does not
        // expose ItemTemplate) - wired to its persistent label the same way LabeledComboField
        // wires its own (SRS UI-14).
        var profileCombo = this.FindControl<ComboBox>("ProfileCombo");
        var profileLabel = this.FindControl<TextBlock>("ProfileLabel");
        if (profileCombo is not null && profileLabel is not null)
        {
            AutomationProperties.SetLabeledBy(profileCombo, profileLabel);
        }
    }

    private ImportTabViewModel? ViewModel => DataContext as ImportTabViewModel;

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
            Title = "Choose a catalogue spreadsheet",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Spreadsheet (*.xlsx, *.xls, *.csv)")
                {
                    Patterns = ["*.xlsx", "*.xls", "*.csv"],
                },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        viewModel.FilePath = files[0].Path.LocalPath;
        await viewModel.LoadHeadersCommand.ExecuteAsync(null);
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the catalogue",
            SuggestedFileName = "catalogue-export.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel workbook (*.xlsx)") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("CSV (*.csv)") { Patterns = ["*.csv"] },
            ],
        });

        if (file is null)
        {
            return;
        }

        await viewModel.ExportAsync(file.Path.LocalPath, CancellationToken.None);
    }
}
