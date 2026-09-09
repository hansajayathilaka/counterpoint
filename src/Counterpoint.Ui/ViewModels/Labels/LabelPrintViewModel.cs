using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Labels;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Labels;

/// <summary>
/// The owner's label-printing screen: search or pick products, set how many labels each needs,
/// preview, and print (SRS FR-2.10, FR-2.12).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no business logic here.</b> The viewmodel holds what the owner typed and picked,
/// calls <see cref="IProductSearchService"/> and <see cref="ILabelPrintService"/>, and shows what
/// comes back - the same shape <c>SalesViewModel</c> keeps, and for the same reason
/// (CLAUDE.md invariant 8, SRS NFR-S2, AC-17). It cannot even reach the TSPL renderer or the
/// printer directly: <c>Counterpoint.Ui</c> may not reference <c>Counterpoint.Devices</c>
/// (CLAUDE.md "Project boundaries").
/// </para>
/// <para>
/// <b>Feeds three sources into the one batch list</b> the task asks for: typing into the search
/// box adds from a result set, and the same <see cref="Batch"/> is exactly what a future GRN
/// screen would populate from a receipt line instead (FR-2.12) - the print action does not care
/// where a row came from.
/// </para>
/// </remarks>
public sealed partial class LabelPrintViewModel : ViewModelBase
{
    private readonly IProductSearchService _search;
    private readonly ILabelPrintService _printer;
    private readonly ISettings _settings;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _status = "Search for a product, or scan its barcode, to add it to the batch.";

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private LabelSearchResultViewModel? _selectedSearchResult;

    [ObservableProperty]
    private LabelBatchLineViewModel? _selectedBatchLine;

    public LabelPrintViewModel(IProductSearchService search, ILabelPrintService printer, ISettings settings)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(settings);

        _search = search;
        _printer = printer;
        _settings = settings;
    }

    /// <summary>What the search box has found so far.</summary>
    public ObservableCollection<LabelSearchResultViewModel> SearchResults { get; } = [];

    /// <summary>The products waiting to be printed, and their quantity-per-label.</summary>
    public ObservableCollection<LabelBatchLineViewModel> Batch { get; } = [];

    /// <summary>Runs the counter search - a product list or a search result set (FR-2.10).</summary>
    [RelayCommand]
    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        SearchResults.Clear();

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        var results = await _search.SearchAsync(SearchText, cancellationToken).ConfigureAwait(true);

        foreach (var result in results)
        {
            SearchResults.Add(new LabelSearchResultViewModel(result));
        }

        Status = results.Count == 0
            ? "Nothing matched that search."
            : string.Create(CultureInfo.InvariantCulture, $"{results.Count} product(s) found.");
    }

    /// <summary>Adds the selected search result to the batch, at the shop's default quantity.</summary>
    [RelayCommand]
    public void AddSelectedToBatch()
    {
        if (SelectedSearchResult is not { } selected)
        {
            return;
        }

        if (Batch.Any(line => line.ProductVariantId == selected.ProductVariantId))
        {
            Status = "That product is already in the batch.";
            return;
        }

        Batch.Add(new LabelBatchLineViewModel(
            selected.ProductVariantId,
            selected.ProductName,
            selected.Sku,
            selected.UomSymbol,
            selected.UnitPriceText,
            _settings.Current.Label.DefaultQuantityPerLabel));

        PreviewText = string.Empty;
        Status = string.Create(CultureInfo.InvariantCulture, $"{selected.ProductName} added.");
    }

    /// <summary>Takes the selected line back out of the batch.</summary>
    [RelayCommand]
    public void RemoveSelectedFromBatch()
    {
        if (SelectedBatchLine is not { } selected)
        {
            return;
        }

        Batch.Remove(selected);
        PreviewText = string.Empty;
    }

    /// <summary>
    /// A plain-text stand-in for what the label will show, one line per product: nothing is sent
    /// to a printer. The exact layout the TSPL renderer draws is <c>HW-T03</c>'s to confirm on
    /// the shop's own label stock; this is enough for the owner to check the batch before
    /// spending labels on it.
    /// </summary>
    [RelayCommand]
    public void Preview()
    {
        if (Batch.Count == 0)
        {
            Status = "Add at least one product to the batch first.";
            return;
        }

        var layout = _settings.Current.Label;
        var builder = new StringBuilder();

        foreach (var line in Batch)
        {
            builder.Append(CultureInfo.InvariantCulture, $"x{line.Quantity}  ");

            if (layout.ShowProductName)
            {
                builder.Append(line.ProductName).Append("  ");
            }

            if (layout.ShowCode)
            {
                builder.Append('[').Append(line.Sku).Append("]  ");
            }

            if (layout.ShowUnit)
            {
                builder.Append(line.UomSymbol).Append("  ");
            }

            if (layout.ShowPrice)
            {
                builder.Append(line.UnitPriceText);
            }

            builder.AppendLine();
        }

        PreviewText = builder.ToString();
        Status = string.Create(
            CultureInfo.InvariantCulture,
            $"Previewing {Batch.Count} product(s) on a {layout.WidthMm} x {layout.HeightMm} mm label.");
    }

    /// <summary>Renders and prints the whole batch in one job.</summary>
    [RelayCommand]
    public async Task PrintAsync(CancellationToken cancellationToken)
    {
        if (Busy)
        {
            return;
        }

        if (Batch.Count == 0)
        {
            Status = "Add at least one product to the batch first.";
            return;
        }

        Busy = true;
        try
        {
            var items = Batch
                .Select(line => new LabelPrintRequestItem(line.ProductVariantId, line.Quantity))
                .ToList();

            var outcome = await _printer.PrintAsync(items, cancellationToken).ConfigureAwait(true);

            Status = outcome.Succeeded
                ? string.Create(CultureInfo.InvariantCulture, $"Printed. ({outcome.Target})")
                : outcome.FailureReason ?? "The labels could not be printed.";
        }
        catch (NotAuthorisedException exception)
        {
            // The Application layer refused the caller, not the value - shown, not worked around
            // (SRS §3.3 ROLE-2, NFR-S2, UI-06, AC-17).
            Status = exception.Message;
        }
        catch (ArgumentException exception)
        {
            Status = PlainLanguage(exception);
        }
        catch (InvalidOperationException exception)
        {
            Status = PlainLanguage(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled. Nothing was printed.";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// The message without the argument name the framework appends to it - the same tidy-up
    /// <c>SettingsViewModel</c> does for the same reason.
    /// </summary>
    private static string PlainLanguage(Exception exception)
    {
        var message = exception.Message;
        var parameter = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        return parameter < 0 ? message : message[..parameter];
    }
}
