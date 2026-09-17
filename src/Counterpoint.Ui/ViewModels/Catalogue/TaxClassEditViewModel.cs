using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one tax class (Q-02, FR-10.3,
/// SRS UI-15, AC-23), following <see cref="CategoryEditViewModel"/>'s pattern exactly.
/// </summary>
public sealed partial class TaxClassEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly ITaxClassMaintenance _taxClasses;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _ratePercentText;

    public TaxClassEditViewModel(
        ITaxClassMaintenance taxClasses,
        long? editingId,
        string initialName,
        string initialRatePercentText)
    {
        ArgumentNullException.ThrowIfNull(taxClasses);
        ArgumentNullException.ThrowIfNull(initialName);
        ArgumentNullException.ThrowIfNull(initialRatePercentText);

        _taxClasses = taxClasses;
        _editingId = editingId;
        _name = initialName;
        _ratePercentText = initialRatePercentText;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveTaxClassCommand(Name, SettingsText.ToTaxRate(RatePercentText));

        if (_editingId is { } id)
        {
            await _taxClasses.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _taxClasses.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
