using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The unit-of-measure tab of the catalogue screen. No "turn off": see the remarks on
/// <c>Counterpoint.Application.Abstractions.Persistence.UomRecord</c> for why.
/// </summary>
public sealed partial class UomTabViewModel : ReferenceDataTabViewModel
{
    private readonly IUomMaintenance _uoms;
    private long? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _symbol = string.Empty;

    [ObservableProperty]
    private string _decimalPlacesText = "0";

    [ObservableProperty]
    private UomRowViewModel? _selectedItem;

    public UomTabViewModel(IUomMaintenance uoms)
    {
        ArgumentNullException.ThrowIfNull(uoms);
        _uoms = uoms;
    }

    public ObservableCollection<UomRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var uoms = await _uoms.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var uom in uoms)
                {
                    Items.Add(new UomRowViewModel(uom));
                }

                Status = Items.Count == 1 ? "1 unit." : Items.Count + " units.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Name = string.Empty;
        Symbol = string.Empty;
        DecimalPlacesText = "0";
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var decimalPlaces = int.TryParse(
                    DecimalPlacesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0;

                var command = new SaveUomCommand(Name, Symbol, decimalPlaces);

                if (_editingId is { } id)
                {
                    await _uoms.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                }
                else
                {
                    await _uoms.CreateAsync(command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " created.";
                }

                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a unit first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _uoms.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(UomRowViewModel? value)
    {
        _editingId = value?.Id;
        Name = value?.Name ?? string.Empty;
        Symbol = value?.Symbol ?? string.Empty;
        DecimalPlacesText = (value?.DecimalPlaces ?? 0).ToString(CultureInfo.InvariantCulture);
    }
}
