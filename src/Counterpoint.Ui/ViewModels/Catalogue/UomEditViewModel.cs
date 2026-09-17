using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one unit of measure (SRS UI-15,
/// AC-23), following <see cref="CategoryEditViewModel"/>'s pattern exactly.
/// </summary>
public sealed partial class UomEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly IUomMaintenance _uoms;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _symbol;

    [ObservableProperty]
    private string _decimalPlacesText;

    public UomEditViewModel(
        IUomMaintenance uoms,
        long? editingId,
        string initialName,
        string initialSymbol,
        string initialDecimalPlacesText)
    {
        ArgumentNullException.ThrowIfNull(uoms);
        ArgumentNullException.ThrowIfNull(initialName);
        ArgumentNullException.ThrowIfNull(initialSymbol);
        ArgumentNullException.ThrowIfNull(initialDecimalPlacesText);

        _uoms = uoms;
        _editingId = editingId;
        _name = initialName;
        _symbol = initialSymbol;
        _decimalPlacesText = initialDecimalPlacesText;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var decimalPlaces = int.TryParse(
            DecimalPlacesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        var command = new SaveUomCommand(Name, Symbol, decimalPlaces);

        if (_editingId is { } id)
        {
            await _uoms.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _uoms.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
