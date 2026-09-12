using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Ui.ViewModels.Catalogue;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>
/// The purchase-order screen: raise, send, cancel and print purchase orders, and the
/// suggested-order report (SRS FR-4.5, FR-4.6, FR-4.10, task P2-T06).
/// </summary>
/// <remarks>
/// Deliberately plain, as the catalogue screen's own tabs are: this is the shape of the
/// operations, not the shape of a finished procurement module (P2-T06's own "Risks": "stay
/// inside the line"). Every command this calls is owner-only in the Application layer, and a
/// refusal is just another <see cref="NotAuthorisedException"/> shown as a sentence (SRS NFR-S2,
/// AC-17) - the same reasoning <c>ReferenceDataTabViewModel</c> already documents.
/// </remarks>
public sealed partial class PurchaseOrderViewModel : ViewModelBase
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly ISupplierMaintenance _suppliers;
    private readonly IProductSearchService _search;

    private CancellationTokenSource? _lineSearchCancellation;

    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private PurchaseOrderSummaryRowViewModel? _selectedOrder;

    [ObservableProperty]
    private PurchaseOrderRecord? _selectedOrderDetail;

    [ObservableProperty]
    private SupplierRowViewModel? _draftSupplier;

    [ObservableProperty]
    private string _draftExpectedDate = string.Empty;

    [ObservableProperty]
    private string _draftNote = string.Empty;

    [ObservableProperty]
    private string _lineSearchQuery = string.Empty;

    [ObservableProperty]
    private PurchaseOrderSearchResultViewModel? _selectedLineSearchResult;

    [ObservableProperty]
    private PurchaseOrderSearchResultViewModel? _pickedProduct;

    [ObservableProperty]
    private ProductUomChoiceViewModel? _selectedUom;

    [ObservableProperty]
    private string _cancelReason = string.Empty;

    public PurchaseOrderViewModel(
        IPurchaseOrderService purchaseOrders,
        ISupplierMaintenance suppliers,
        IProductSearchService search)
    {
        ArgumentNullException.ThrowIfNull(purchaseOrders);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(search);

        _purchaseOrders = purchaseOrders;
        _suppliers = suppliers;
        _search = search;
    }

    public ObservableCollection<PurchaseOrderSummaryRowViewModel> Orders { get; } = [];

    public ObservableCollection<SupplierRowViewModel> Suppliers { get; } = [];

    public ObservableCollection<SuggestedOrderRowViewModel> SuggestedOrders { get; } = [];

    public ObservableCollection<PurchaseOrderDraftLineViewModel> DraftLines { get; } = [];

    public ObservableCollection<PurchaseOrderSearchResultViewModel> LineSearchResults { get; } = [];

    public ObservableCollection<ProductUomChoiceViewModel> PickedProductUoms { get; } = [];

    /// <summary>The selected order's lines, in the shape the detail panel shows them.</summary>
    public ObservableCollection<PurchaseOrderLineRowViewModel> SelectedOrderLines { get; } = [];

    /// <summary>True only while the selected order is still <c>DRAFT</c> - the one status "Send" applies to.</summary>
    public bool CanSendSelected => string.Equals(SelectedOrderDetail?.Status, "DRAFT", StringComparison.Ordinal);

    /// <summary>True while the selected order is neither fully received nor already cancelled.</summary>
    public bool CanCancelSelected =>
        SelectedOrderDetail is { } order
        && !string.Equals(order.Status, "RECEIVED", StringComparison.Ordinal)
        && !string.Equals(order.Status, "CANCELLED", StringComparison.Ordinal);

    /// <summary>Whether the detail panel has anything to show - so the XAML can hide it with a plain bool binding.</summary>
    public bool HasSelectedOrder => SelectedOrderDetail is not null;

    /// <summary>Whether a product has been picked for the line being built.</summary>
    public bool HasPickedProduct => PickedProduct is not null;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var orders = await _purchaseOrders.ListAsync(cancellationToken).ConfigureAwait(true);

                Orders.Clear();
                foreach (var order in orders)
                {
                    Orders.Add(new PurchaseOrderSummaryRowViewModel(order));
                }

                var suppliers = await _suppliers.ListAsync(cancellationToken).ConfigureAwait(true);
                Suppliers.Clear();
                foreach (var supplier in suppliers.Where(s => s.Active))
                {
                    Suppliers.Add(new SupplierRowViewModel(supplier));
                }

                Status = Orders.Count == 1 ? "1 purchase order." : Orders.Count + " purchase orders.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RefreshSuggestedOrderAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var lines = await _purchaseOrders.GetSuggestedOrderAsync(cancellationToken).ConfigureAwait(true);

                SuggestedOrders.Clear();
                foreach (var line in lines)
                {
                    SuggestedOrders.Add(new SuggestedOrderRowViewModel(line));
                }

                Status = SuggestedOrders.Count == 0
                    ? "Nothing is at or below its reorder level."
                    : SuggestedOrders.Count + " item(s) at or below reorder level.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedOrderChanged(PurchaseOrderSummaryRowViewModel? value)
    {
        _ = LoadSelectedOrderAsync(value, CancellationToken.None);
    }

    private async Task LoadSelectedOrderAsync(PurchaseOrderSummaryRowViewModel? selected, CancellationToken cancellationToken)
    {
        if (selected is null)
        {
            SelectedOrderDetail = null;
            return;
        }

        await RunAsync(
            async () =>
            {
                SelectedOrderDetail = await _purchaseOrders.FindByIdAsync(selected.Id, cancellationToken)
                    .ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedOrderDetailChanged(PurchaseOrderRecord? value)
    {
        OnPropertyChanged(nameof(CanSendSelected));
        OnPropertyChanged(nameof(CanCancelSelected));
        OnPropertyChanged(nameof(HasSelectedOrder));

        SelectedOrderLines.Clear();
        if (value is null)
        {
            return;
        }

        foreach (var line in value.Lines)
        {
            SelectedOrderLines.Add(new PurchaseOrderLineRowViewModel(line));
        }
    }

    partial void OnPickedProductChanged(PurchaseOrderSearchResultViewModel? value) =>
        OnPropertyChanged(nameof(HasPickedProduct));

    // ---- Building a new order --------------------------------------------------------------

    [RelayCommand]
    public void NewOrder()
    {
        DraftSupplier = null;
        DraftExpectedDate = string.Empty;
        DraftNote = string.Empty;
        DraftLines.Clear();
        ClearLineEntry();
    }

    partial void OnLineSearchQueryChanged(string value)
    {
        _lineSearchCancellation?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            LineSearchResults.Clear();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _lineSearchCancellation = cancellation;
        _ = RunLineSearchAsync(value, cancellation.Token);
    }

    private async Task RunLineSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var results = await _search.SearchAsync(query, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            LineSearchResults.Clear();
            foreach (var result in results)
            {
                LineSearchResults.Add(new PurchaseOrderSearchResultViewModel(result));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke - nothing to show for it.
        }
    }

    [RelayCommand]
    public async Task PickLineProductAsync(PurchaseOrderSearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                PickedProduct = result;

                var uoms = await _purchaseOrders.GetOrderingUnitsAsync(result.ProductVariantId, CancellationToken.None)
                    .ConfigureAwait(true);

                PickedProductUoms.Clear();
                foreach (var uom in uoms)
                {
                    PickedProductUoms.Add(new ProductUomChoiceViewModel(uom));
                }

                SelectedUom = PickedProductUoms.FirstOrDefault(u => u.DisplayText.EndsWith("(base unit)", StringComparison.Ordinal))
                    ?? PickedProductUoms.FirstOrDefault();
            },
            CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    public void AddDraftLine()
    {
        if (PickedProduct is not { } product || SelectedUom is not { } uom)
        {
            Status = "Pick a product and a unit first.";
            return;
        }

        DraftLines.Add(new PurchaseOrderDraftLineViewModel(
            product.ProductVariantId, product.Sku, product.ProductName, uom.UomId, uom.Symbol));

        ClearLineEntry();
    }

    [RelayCommand]
    public void RemoveDraftLine(PurchaseOrderDraftLineViewModel? line)
    {
        if (line is not null)
        {
            DraftLines.Remove(line);
        }
    }

    private void ClearLineEntry()
    {
        LineSearchQuery = string.Empty;
        LineSearchResults.Clear();
        PickedProduct = null;
        PickedProductUoms.Clear();
        SelectedUom = null;
    }

    [RelayCommand]
    public async Task SaveDraftAsync(CancellationToken cancellationToken)
    {
        if (DraftSupplier is not { } supplier)
        {
            Status = "Pick a supplier first.";
            return;
        }

        if (DraftLines.Count == 0)
        {
            Status = "Add at least one line first.";
            return;
        }

        var lines = new List<CreatePurchaseOrderLineCommand>(DraftLines.Count);
        foreach (var line in DraftLines)
        {
            if (!decimal.TryParse(line.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0m)
            {
                Status = "'" + line.ProductDescription + "': the quantity must be a number greater than zero.";
                return;
            }

            if (!decimal.TryParse(line.UnitCost, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitCost)
                || unitCost < 0m)
            {
                Status = "'" + line.ProductDescription + "': the unit cost must be a number, zero or more.";
                return;
            }

            lines.Add(new CreatePurchaseOrderLineCommand(line.ProductVariantId, line.UomId, quantity, Money.FromDecimal(unitCost)));
        }

        DateTimeOffset? expectedAt = null;
        if (!string.IsNullOrWhiteSpace(DraftExpectedDate))
        {
            if (!DateOnly.TryParse(DraftExpectedDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expectedDate))
            {
                Status = "The expected date must be a date, for example 2026-09-20.";
                return;
            }

            expectedAt = new DateTimeOffset(expectedDate.ToDateTime(TimeOnly.MinValue));
        }

        await RunAsync(
            async () =>
            {
                var created = await _purchaseOrders.CreateAsync(
                    new CreatePurchaseOrderCommand(supplier.Id, expectedAt, NullIfBlank(DraftNote), lines),
                    cancellationToken).ConfigureAwait(true);

                Status = "Purchase order " + created.PoNo + " created.";
                NewOrder();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    // ---- Acting on an existing order --------------------------------------------------------

    [RelayCommand]
    public async Task SendAsync(CancellationToken cancellationToken)
    {
        if (SelectedOrderDetail is not { } order)
        {
            Status = "Pick a purchase order first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _purchaseOrders.SendAsync(order.Id, cancellationToken).ConfigureAwait(true);
                Status = "Purchase order " + order.PoNo + " sent.";
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                await LoadSelectedOrderAsync(SelectedOrder, cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        if (SelectedOrderDetail is not { } order)
        {
            Status = "Pick a purchase order first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _purchaseOrders.CancelAsync(order.Id, CancelReason, cancellationToken).ConfigureAwait(true);
                CancelReason = string.Empty;
                Status = "Purchase order " + order.PoNo + " cancelled.";
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                await LoadSelectedOrderAsync(SelectedOrder, cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task PrintAsync(CancellationToken cancellationToken)
    {
        if (SelectedOrderDetail is not { } order)
        {
            Status = "Pick a purchase order first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _purchaseOrders.PrintAsync(order.Id, cancellationToken).ConfigureAwait(true);
                Status = "Purchase order " + order.PoNo + " queued to print.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Runs an Application call with the screen locked, and turns anything that comes back into a
    /// sentence the owner can act on (SRS UI-06) - the same wrapper
    /// <c>ReferenceDataTabViewModel.RunAsync</c> gives the catalogue screen's tabs.
    /// </summary>
    private async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        var alreadyBusy = Busy;
        Busy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (NotAuthorisedException exception)
        {
            Status = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        finally
        {
            Busy = alreadyBusy;
        }
    }
}
