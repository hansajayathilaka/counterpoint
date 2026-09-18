using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one brand (SRS UI-15, AC-23,
/// FR-2.21), following <see cref="CategoryEditViewModel"/>'s pattern exactly.
/// </summary>
/// <remarks>
/// Holds nothing about whether it is creating or editing beyond <c>_editingId</c>, which decides
/// which <see cref="IBrandMaintenance"/> call <see cref="SaveAsync"/> makes - it never decides
/// what the dialog's header says. That is <see cref="BrandTabViewModel"/>'s job, driven by the
/// explicit <see cref="DialogMode"/> it passes to <see cref="IDialogService"/>.
/// </remarks>
public sealed partial class BrandEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly IBrandMaintenance _brands;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _name;

    public BrandEditViewModel(IBrandMaintenance brands, long? editingId, string initialName)
    {
        ArgumentNullException.ThrowIfNull(brands);
        ArgumentNullException.ThrowIfNull(initialName);

        _brands = brands;
        _editingId = editingId;
        _name = initialName;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveBrandCommand(Name);

        if (_editingId is { } id)
        {
            await _brands.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _brands.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
