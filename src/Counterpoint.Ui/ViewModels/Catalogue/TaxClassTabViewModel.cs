using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The tax-class tab of the catalogue screen (Q-02, FR-10.3).</summary>
public sealed partial class TaxClassTabViewModel : ReferenceDataTabViewModel
{
    private readonly ITaxClassMaintenance _taxClasses;
    private long? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _ratePercentText = "0";

    [ObservableProperty]
    private TaxClassRowViewModel? _selectedItem;

    public TaxClassTabViewModel(ITaxClassMaintenance taxClasses)
    {
        ArgumentNullException.ThrowIfNull(taxClasses);
        _taxClasses = taxClasses;
    }

    public ObservableCollection<TaxClassRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var taxClasses = await _taxClasses.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var taxClass in taxClasses)
                {
                    Items.Add(new TaxClassRowViewModel(taxClass));
                }

                Status = Items.Count == 1 ? "1 tax class." : Items.Count + " tax classes.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Name = string.Empty;
        RatePercentText = "0";
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var command = new SaveTaxClassCommand(Name, SettingsText.ToTaxRate(RatePercentText));

                if (_editingId is { } id)
                {
                    await _taxClasses.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                }
                else
                {
                    await _taxClasses.CreateAsync(command, cancellationToken).ConfigureAwait(true);
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
            Status = "Pick a tax class first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _taxClasses.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _taxClasses.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
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
            Status = "Pick a tax class first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _taxClasses.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(TaxClassRowViewModel? value)
    {
        _editingId = value?.Id;
        Name = value?.Name ?? string.Empty;
        RatePercentText = value?.RatePercentText ?? "0";
    }
}
