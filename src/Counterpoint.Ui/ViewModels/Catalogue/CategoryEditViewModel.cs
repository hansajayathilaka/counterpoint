using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T11's dialog when creating or editing one category (SRS UI-15, AC-23,
/// FR-2.20) - the proof-of-concept screen this task converts to the shared dialog shell.
/// </summary>
/// <remarks>
/// Holds nothing about whether it is creating or editing beyond <c>_editingId</c>, which decides
/// which <see cref="ICategoryMaintenance"/> call <see cref="SaveAsync"/> makes - it never decides
/// what the dialog's header says. That is <see cref="CategoryTabViewModel"/>'s job, driven by the
/// explicit <see cref="DialogMode"/> it passes to <see cref="IDialogService"/>.
/// </remarks>
public sealed partial class CategoryEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly ICategoryMaintenance _categories;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private CategoryParentOption? _selectedParent;

    public CategoryEditViewModel(
        ICategoryMaintenance categories,
        IReadOnlyList<CategoryParentOption> parentOptions,
        long? editingId,
        string initialName,
        CategoryParentOption? initialParent)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(parentOptions);
        ArgumentNullException.ThrowIfNull(initialName);

        _categories = categories;
        _editingId = editingId;
        _name = initialName;
        _selectedParent = initialParent;

        ParentOptions = new ObservableCollection<CategoryParentOption>(parentOptions);
    }

    /// <summary>Top-level categories, plus "(top level)" itself - the only legal parents (FR-2.20).</summary>
    public ObservableCollection<CategoryParentOption> ParentOptions { get; }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveCategoryCommand(Name, SelectedParent?.Id);

        if (_editingId is { } id)
        {
            await _categories.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _categories.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
