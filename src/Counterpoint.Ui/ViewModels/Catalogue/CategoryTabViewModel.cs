using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The category tab of the catalogue screen (SRS FR-2.20).</summary>
public sealed partial class CategoryTabViewModel : ReferenceDataTabViewModel
{
    private readonly ICategoryMaintenance _categories;
    private long? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private CategoryParentOption? _selectedParent;

    [ObservableProperty]
    private CategoryRowViewModel? _selectedItem;

    public CategoryTabViewModel(ICategoryMaintenance categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        _categories = categories;
    }

    public ObservableCollection<CategoryRowViewModel> Items { get; } = [];

    /// <summary>Top-level categories, plus "(top level)" itself - the only legal parents (FR-2.20).</summary>
    public ObservableCollection<CategoryParentOption> ParentOptions { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var categories = await _categories.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var category in categories)
                {
                    Items.Add(new CategoryRowViewModel(category));
                }

                ParentOptions.Clear();
                ParentOptions.Add(new CategoryParentOption(null, "(top level)"));
                foreach (var category in categories.Where(c => c.ParentId is null))
                {
                    ParentOptions.Add(new CategoryParentOption(category.Id, category.Name));
                }

                Status = Items.Count == 1 ? "1 category." : Items.Count + " categories.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Clears the form so the next Save creates a new category.</summary>
    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Name = string.Empty;
        SelectedParent = ParentOptions.FirstOrDefault();
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var command = new SaveCategoryCommand(Name, SelectedParent?.Id);

                if (_editingId is { } id)
                {
                    await _categories.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                }
                else
                {
                    await _categories.CreateAsync(command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " created.";
                }

                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a category first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _categories.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _categories.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a category first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _categories.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(CategoryRowViewModel? value)
    {
        _editingId = value?.Id;
        Name = value?.Name ?? string.Empty;
        SelectedParent = value is null
            ? ParentOptions.FirstOrDefault()
            : ParentOptions.FirstOrDefault(option => option.Id == value.ParentId) ?? ParentOptions.FirstOrDefault();
    }
}
