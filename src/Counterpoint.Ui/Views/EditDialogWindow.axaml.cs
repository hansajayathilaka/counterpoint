using Avalonia.Controls;
using Counterpoint.Ui.ViewModels.Dialogs;

namespace Counterpoint.Ui.Views;

/// <summary>
/// Task P3-T11's shared add/edit/delete dialog shell (SRS UI-05, UI-06, UI-15, AC-23). Markup and
/// a single wire-up: when <see cref="EditDialogWindowViewModel.CloseRequested"/> fires, this
/// window closes itself. Everything else - what the header says, which footer is shown - is a
/// binding to the viewmodel.
/// </summary>
public partial class EditDialogWindow : Window
{
    public EditDialogWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is EditDialogWindowViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
