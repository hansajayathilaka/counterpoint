using System;
using Avalonia.Controls;
using Counterpoint.Ui.ViewModels.Settings;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The owner's settings screen (SRS FR-10).
/// </summary>
/// <remarks>
/// Markup and one piece of plumbing: the viewmodel asks to be closed and the window closes.
/// Everything else is a binding.
/// </remarks>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // The viewmodel outlives the window - it is a singleton, and the screen can be opened
        // again. A handler left behind would ask a closed window to close itself.
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
