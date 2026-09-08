using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The brand tab of the catalogue screen (SRS FR-2.21).</summary>
public sealed partial class BrandTabViewModel : ReferenceDataTabViewModel
{
    private readonly IBrandMaintenance _brands;
    private long? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private BrandRowViewModel? _selectedItem;

    public BrandTabViewModel(IBrandMaintenance brands)
    {
        ArgumentNullException.ThrowIfNull(brands);
        _brands = brands;
    }

    public ObservableCollection<BrandRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var brands = await _brands.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var brand in brands)
                {
                    Items.Add(new BrandRowViewModel(brand));
                }

                Status = Items.Count == 1 ? "1 brand." : Items.Count + " brands.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Name = string.Empty;
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var command = new SaveBrandCommand(Name);

                if (_editingId is { } id)
                {
                    await _brands.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                }
                else
                {
                    await _brands.CreateAsync(command, cancellationToken).ConfigureAwait(true);
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
            Status = "Pick a brand first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _brands.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _brands.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
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
            Status = "Pick a brand first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _brands.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(BrandRowViewModel? value)
    {
        _editingId = value?.Id;
        Name = value?.Name ?? string.Empty;
    }
}
