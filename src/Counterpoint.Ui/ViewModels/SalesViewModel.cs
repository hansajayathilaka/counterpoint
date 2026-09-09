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
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Ui.ViewModels;

/// <summary>Which non-modal side panel is open, if any (UI-01: the mouse stays optional throughout).</summary>
public enum SalesPanel
{
    None,
    Help,
    Search,
    Hold,
    HeldBills,
    Discount,
    Customer,
    StockEnquiry,
    OpenItem,
}

/// <summary>
/// The sales screen: a scan box, the lines on the bill, the total, and every function key of
/// SRS UI-02 (SRS FR-3.1-FR-3.15, UI-01-UI-12, NFR-U1, NFR-U2).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no business logic here, and there must never be.</b> The viewmodel holds what
/// the cashier has typed, calls the Application layer's <c>IScanItem</c>, <c>IQuoteSale</c> and
/// <c>ICompleteSale</c>, and shows what comes back. It does not price a line, apply a rounding
/// rule, decide a discount cap or a negative-stock policy, or know that SQLite exists (CLAUDE.md
/// invariant 8, SRS NFR-S2, AC-17). Every edit a cashier makes - a quantity, a unit, a discount,
/// an open item, a held bill - is re-quoted through <c>IQuoteSale</c> before it is shown; nothing
/// here multiplies a price by a quantity.
/// </para>
/// <para>
/// <b>Deliberately out of this task's reach</b> (see the task's own notes and
/// <c>docs/03_PHASE_1_core_trading.md</c>): F4 Return (Phase 2), a split-tender pay dialog with
/// change (P1-T10 - F9 here still completes a bill, at the exact cash amount, which is enough to
/// prove NFR-U2's keystroke budget), F10 Reprint (needs P1-T11's receipt/print-queue machinery),
/// F12 Day Close (needs Phase 3's shift close), and an owner re-authentication dialog for a
/// discount above its cap (SRS FR-3.18) or a manual price override (FR-3.19) - nothing in Phase 1
/// builds that dialog yet, so an over-cap discount is refused with a plain-language message
/// rather than offered an override. Trade price tiers (FR-3.23) are Phase 5's, explicitly out of
/// scope for Phase 1 (docs/03_PHASE_1_core_trading.md); attaching a customer here (F8) is for
/// record-keeping only and does not change a line's price.
/// </para>
/// </remarks>
public sealed partial class SalesViewModel : NumericInputViewModel
{
    private readonly IScanItem _scanner;
    private readonly IQuoteSale _quoter;
    private readonly ICompleteSale _sales;
    private readonly ITillSessionProvider _sessions;
    private readonly ISession _session;
    private readonly ISettings _settings;
    private readonly IProductSearchService _search;
    private readonly ICustomerStore _customers;
    private readonly IUomStore _uoms;
    private readonly IStockEnquiry _stockEnquiry;
    private readonly IHeldBillService _heldBills;
    private readonly TimeProvider _timeProvider;

    private readonly List<SaleLineRequest> _bill = [];
    private readonly List<IReadOnlyList<ScannedItemUnitOption>> _lineUnitOptions = [];

    private long? _customerId;
    private string? _customerName;
    private DiscountInput? _pendingBillDiscount;
    private long? _lastScannedVariantId;
    private bool _pendingClearConfirmation;
    private IReadOnlyList<CustomerRecord> _allCustomers = [];
    private CancellationTokenSource? _searchCancellation;

    [ObservableProperty]
    private string _barcode = string.Empty;

    [ObservableProperty]
    private string _subtotalText = "0.00";

    [ObservableProperty]
    private string _billDiscountText = string.Empty;

    /// <summary>Whether a whole-bill discount is in force - what the total block shows a column for.</summary>
    public bool HasBillDiscount => BillDiscountText.Length > 0;

    partial void OnBillDiscountTextChanged(string value) => OnPropertyChanged(nameof(HasBillDiscount));

    [ObservableProperty]
    private string _taxText = "0.00";

    /// <summary>The grand total - the most prominent figure on the screen (SRS UI-03, FR-3.10).</summary>
    [ObservableProperty]
    private string _total = "0.00";

    [ObservableProperty]
    private string _status = "Scan an item to start a bill.";

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private SaleLineViewModel? _selectedLine;

    private SalesPanel _activePanel;

    // ---- Search panel (F3, SRS FR-2.11, FR-3.3) ------------------------------------------------

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public ObservableCollection<SalesSearchResultViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    private SalesSearchResultViewModel? _selectedSearchResult;

    // ---- Hold panel (F5, SRS FR-3.32) -----------------------------------------------------------

    [ObservableProperty]
    private string _holdLabel = string.Empty;

    // ---- Held-bill list (F6, SRS FR-3.32, FR-3.33) -----------------------------------------------

    public ObservableCollection<HeldBillRowViewModel> HeldBillRows { get; } = [];

    [ObservableProperty]
    private HeldBillRowViewModel? _selectedHeldBill;

    // ---- Discount panel (F7, SRS FR-3.16, FR-3.17) -----------------------------------------------

    [ObservableProperty]
    private bool _discountIsPercent = true;

    private string _discountValueText = string.Empty;

    public string DiscountValueText
    {
        get => _discountValueText;
        set => SetNumeric(ref _discountValueText, value, allowDecimal: true);
    }

    public string DiscountTargetText => SelectedLine is { } line
        ? "This line: " + line.Description
        : "The whole bill";

    // ---- Customer panel (F8, SRS FR-3.21, FR-3.22) -----------------------------------------------

    [ObservableProperty]
    private string _customerQuery = string.Empty;

    public ObservableCollection<CustomerSearchResultViewModel> CustomerResults { get; } = [];

    [ObservableProperty]
    private CustomerSearchResultViewModel? _selectedCustomerResult;

    public string CurrentCustomerText => _customerName is { } name ? name : "Walk-in (no customer)";

    // ---- Stock enquiry panel (F11, SRS FR-4, FR-3.11) ---------------------------------------------

    [ObservableProperty]
    private string _stockEnquiryText = string.Empty;

    // ---- Open item panel (F R-2.8) ----------------------------------------------------------------

    [ObservableProperty]
    private string _openItemDescription = string.Empty;

    private string _openItemPriceText = string.Empty;

    public string OpenItemPriceText
    {
        get => _openItemPriceText;
        set => SetNumeric(ref _openItemPriceText, value, allowDecimal: true);
    }

    private string _openItemQuantityText = "1";

    public string OpenItemQuantityText
    {
        get => _openItemQuantityText;
        set => SetNumeric(ref _openItemQuantityText, value, allowDecimal: true);
    }

    public ObservableCollection<string> OpenItemUnitChoices { get; } = [];

    [ObservableProperty]
    private string _openItemSelectedUnit = string.Empty;

    private Dictionary<string, long> _openItemUnitIds = [];

    // ---- Help panel (UI-12) ----------------------------------------------------------------------

    /// <summary>The on-screen cheat sheet (SRS UI-12: "the ten most common tasks").</summary>
    public string HelpText { get; } =
        """
        F1  Help              - show / hide this panel
        F2  New sale           - clear the bill (press twice to confirm)
        F3  Search item        - find an item by name, code or SKU
        F4  Return              - not available yet (Phase 2)
        F5  Hold                - park the bill on the counter, labelled
        F6  Recall               - bring back a held bill
        F7  Discount             - a percentage or amount off the selected line, or the whole bill
        F8  Customer             - attach a customer by name or phone, or clear it
        F9  Pay                  - complete the bill for cash, tendered exactly
        F10 Reprint               - not available yet (needs the receipt/print-queue task)
        F11 Stock                 - stock on hand for the selected or last-scanned item
        F12 Day close              - not available yet (Phase 3)
        Esc Cancel                  - close whatever panel is open, or clear the scan box
        """;

    // ---- Status bar (SRS UI-09) --------------------------------------------------------------------

    public string StatusUserText => _session.CurrentUser is { } user
        ? user.DisplayName + " (" + (user.Role == Role.Owner ? "owner" : "cashier") + ")"
        : "Not signed in";

    public string StatusShiftText => _session.ShiftId is { } shiftId
        ? "Shift #" + shiftId.ToString(CultureInfo.InvariantCulture)
        : "No shift open";

    /// <summary>
    /// Honest, not wired: the last-backup timestamp is P1-T15's (local/USB backup does not exist
    /// yet in this task's dependency set), so this names what is missing rather than a number.
    /// </summary>
    public string StatusBackupText { get; } = "Backup: not tracked yet";

    public string StatusCloudText => "Cloud: " +
        (_settings.Current.Backup.CloudTarget == CloudBackupTarget.None ? "off" : _settings.Current.Backup.CloudTarget.ToString());

    public string StatusPrinterText => string.IsNullOrWhiteSpace(_settings.Current.Peripherals.ReceiptPrinterName)
        ? "Printer: file (development)"
        : "Printer: " + _settings.Current.Peripherals.ReceiptPrinterName;

    // ---- What the scanner keystroke filter (SalesWindow's code-behind) reads ------------------
    // Exposed rather than handed the whole ISettings: the window's job is reading real key
    // events off real hardware, not deciding a business rule, so it gets exactly the two numbers
    // it needs and nothing that would let it read a setting the sales screen has no business
    // reading (SRS FR-10.6, P1-T09 "Do this" #4).

    /// <summary>The shortest run the till treats as a scan rather than as typing.</summary>
    public int ScannerMinimumLength => _settings.Current.Peripherals.ScannerMinimumLength;

    /// <summary>What the scanner sends at the end of a scan (SRS FR-10.6).</summary>
    public ScannerSuffix ScannerSuffixSetting => _settings.Current.Peripherals.ScannerSuffix;

    public SalesViewModel(
        IScanItem scanner,
        IQuoteSale quoter,
        ICompleteSale sales,
        ITillSessionProvider sessions,
        ISession session,
        ISettings settings,
        IProductSearchService search,
        ICustomerStore customers,
        IUomStore uoms,
        IStockEnquiry stockEnquiry,
        IHeldBillService heldBills,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(quoter);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(uoms);
        ArgumentNullException.ThrowIfNull(stockEnquiry);
        ArgumentNullException.ThrowIfNull(heldBills);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scanner = scanner;
        _quoter = quoter;
        _sales = sales;
        _sessions = sessions;
        _session = session;
        _settings = settings;
        _search = search;
        _customers = customers;
        _uoms = uoms;
        _stockEnquiry = stockEnquiry;
        _heldBills = heldBills;
        _timeProvider = timeProvider;
    }

    /// <summary>Raised when the cashier asks for the user-management screen.</summary>
    public event EventHandler? ManageUsersRequested;

    /// <summary>Raised when the cashier asks for the settings screen (SRS FR-10).</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the cashier asks for the catalogue reference-data screen.</summary>
    public event EventHandler? CatalogueRequested;

    /// <summary>Raised when the owner asks for the label-printing screen.</summary>
    public event EventHandler? LabelPrintRequested;

    /// <summary>The lines on the bill, in the order they were scanned.</summary>
    public ObservableCollection<SaleLineViewModel> Lines { get; } = [];

    public bool CanManageUsers => _session.CurrentUser?.Role == Role.Owner;

    public bool CanChangeSettings => _session.CurrentUser?.Role == Role.Owner;

    public bool CanManageCatalogue => _session.CurrentUser?.Role == Role.Owner;

    public bool CanPrintLabels => _session.CurrentUser?.Role == Role.Owner;

    public bool IsHelpPanelOpen => _activePanel == SalesPanel.Help;

    public bool IsSearchPanelOpen => _activePanel == SalesPanel.Search;

    public bool IsHoldPanelOpen => _activePanel == SalesPanel.Hold;

    public bool IsHeldBillsPanelOpen => _activePanel == SalesPanel.HeldBills;

    public bool IsDiscountPanelOpen => _activePanel == SalesPanel.Discount;

    public bool IsCustomerPanelOpen => _activePanel == SalesPanel.Customer;

    public bool IsStockPanelOpen => _activePanel == SalesPanel.StockEnquiry;

    public bool IsOpenItemPanelOpen => _activePanel == SalesPanel.OpenItem;

    public bool IsAnyPanelOpen => _activePanel != SalesPanel.None;

    [RelayCommand]
    public void ManageUsers() => ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManageCatalogue() => CatalogueRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void PrintLabels() => LabelPrintRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // ---- F1 Help ------------------------------------------------------------------------------

    [RelayCommand]
    public void Help() => TogglePanel(SalesPanel.Help);

    // ---- F2 New sale (SRS FR-3.5 - "clear the whole bill (with confirmation)") -----------------

    [RelayCommand]
    public void NewSale()
    {
        if (_bill.Count == 0)
        {
            Status = "The bill is already empty.";
            return;
        }

        if (!_pendingClearConfirmation)
        {
            _pendingClearConfirmation = true;
            Status = string.Create(
                CultureInfo.InvariantCulture,
                $"Press F2 again to clear {_bill.Count} item(s), or Esc to keep them.");
            return;
        }

        _pendingClearConfirmation = false;
        ClearBillState();
        Status = "New sale.";
    }

    // ---- F3 Search item (SRS FR-2.11, FR-3.3) --------------------------------------------------

    [RelayCommand]
    public void Search()
    {
        TogglePanel(SalesPanel.Search);
        SearchQuery = string.Empty;
        SearchResults.Clear();
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCancellation?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _ = RunSearchAsync(value, cancellation.Token);
    }

    private async Task RunSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var results = await _search.SearchAsync(query, cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(new SalesSearchResultViewModel(result));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke (NFR-P2). Nothing to show for it.
        }
    }

    [RelayCommand]
    public async Task PickSearchResultAsync(SalesSearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var item = await _scanner.ScanByVariantIdAsync(result.ProductVariantId, CancellationToken.None)
                .ConfigureAwait(true);

            if (item is null)
            {
                Status = "That item is no longer sellable.";
                return;
            }

            await AddOrIncrementAsync(item, CancellationToken.None).ConfigureAwait(true);
            ClosePanel();
        }, CancellationToken.None).ConfigureAwait(true);
    }

    // ---- F4 Return (Phase 2) -------------------------------------------------------------------

    [RelayCommand]
    public void Return() => Status = "Returns are not available yet - they land in Phase 2 (P2-T01, P2-T02).";

    // ---- F5 Hold (SRS FR-3.32, FR-3.33) ---------------------------------------------------------

    [RelayCommand]
    public void Hold()
    {
        if (_bill.Count == 0)
        {
            Status = "There is nothing on the bill to hold.";
            return;
        }

        HoldLabel = string.Empty;
        TogglePanel(SalesPanel.Hold);
    }

    [RelayCommand]
    public async Task ConfirmHoldAsync()
    {
        await RunAsync(async () =>
        {
            var session = await _sessions.GetCurrentAsync(CancellationToken.None).ConfigureAwait(true);
            var userId = session?.UserId ?? _session.CurrentUser?.Id
                ?? throw new InvalidOperationException("Nobody is signed in.");

            var label = string.IsNullOrWhiteSpace(HoldLabel)
                ? "Held " + _timeProvider.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : HoldLabel.Trim();

            await _heldBills.HoldAsync(label, BuildHeldBillPayload(), userId, CancellationToken.None)
                .ConfigureAwait(true);

            ClearBillState();
            Status = "Held as '" + label + "'.";
        }, CancellationToken.None).ConfigureAwait(true);
    }

    // ---- F6 Recall (SRS FR-3.32, FR-3.33) --------------------------------------------------------

    [RelayCommand]
    public async Task RecallAsync()
    {
        TogglePanel(SalesPanel.HeldBills);

        if (!IsHeldBillsPanelOpen)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var rows = await _heldBills.ListAsync(CancellationToken.None).ConfigureAwait(true);

            HeldBillRows.Clear();
            foreach (var row in rows)
            {
                HeldBillRows.Add(new HeldBillRowViewModel(row));
            }
        }, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RecallSelectedAsync(HeldBillRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var payload = await _heldBills.RecallAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            await RestoreFromPayloadAsync(payload, CancellationToken.None).ConfigureAwait(true);
            ClosePanel();
            Status = "Recalled '" + row.Label + "'.";
        }, CancellationToken.None).ConfigureAwait(true);
    }

    // ---- F7 Discount (SRS FR-3.16, FR-3.17, FR-3.18) ---------------------------------------------

    [RelayCommand]
    public void Discount()
    {
        if (_bill.Count == 0)
        {
            Status = "There is nothing on the bill to discount.";
            return;
        }

        DiscountValueText = string.Empty;
        DiscountIsPercent = true;
        TogglePanel(SalesPanel.Discount);
    }

    [RelayCommand]
    public async Task ApplyDiscountAsync()
    {
        var typed = SettingsTextToDecimal(DiscountValueText);

        var discount = DiscountIsPercent
            ? DiscountInput.OfRate(Percentage.FromPercent(typed))
            : DiscountInput.OfAmount(Money.FromDecimal(typed));

        var selectedLine = SelectedLine;

        await RunAsync(
            () => MutateAndRefreshAsync(
                () =>
                {
                    if (selectedLine is { } line)
                    {
                        _bill[line.Index] = _bill[line.Index] with { Discount = discount };
                    }
                    else
                    {
                        _pendingBillDiscount = discount;
                    }
                },
                null,
                CancellationToken.None),
            CancellationToken.None).ConfigureAwait(true);
        ClosePanel();
    }

    [RelayCommand]
    public async Task ClearDiscountAsync()
    {
        if (SelectedLine is { } line)
        {
            _bill[line.Index] = _bill[line.Index] with { Discount = null };
        }
        else
        {
            _pendingBillDiscount = null;
        }

        await RunAsync(() => RefreshAsync(CancellationToken.None), CancellationToken.None).ConfigureAwait(true);
        ClosePanel();
    }

    // ---- F8 Customer (SRS FR-3.21, FR-3.22) -------------------------------------------------------

    [RelayCommand]
    public async Task CustomerAsync()
    {
        TogglePanel(SalesPanel.Customer);

        if (!IsCustomerPanelOpen)
        {
            return;
        }

        CustomerQuery = string.Empty;

        await RunAsync(async () =>
        {
            _allCustomers = await _customers.ListAsync(CancellationToken.None).ConfigureAwait(true);
            RefreshCustomerResults();
        }, CancellationToken.None).ConfigureAwait(true);
    }

    partial void OnCustomerQueryChanged(string value) => RefreshCustomerResults();

    private void RefreshCustomerResults()
    {
        CustomerResults.Clear();

        var query = CustomerQuery.Trim();
        var matches = query.Length == 0
            ? _allCustomers
            : [.. _allCustomers.Where(customer =>
                customer.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (customer.Phone is { } phone && phone.Contains(query, StringComparison.OrdinalIgnoreCase)))];

        foreach (var customer in matches.Where(c => c.Active).Take(50))
        {
            CustomerResults.Add(new CustomerSearchResultViewModel(customer));
        }
    }

    [RelayCommand]
    public void PickCustomer(CustomerSearchResultViewModel? customer)
    {
        if (customer is null)
        {
            return;
        }

        _customerId = customer.Id;
        _customerName = customer.Name;
        OnPropertyChanged(nameof(CurrentCustomerText));
        Status = "Customer: " + customer.Name;
        ClosePanel();
    }

    [RelayCommand]
    public void ClearCustomer()
    {
        _customerId = null;
        _customerName = null;
        OnPropertyChanged(nameof(CurrentCustomerText));
        Status = "Walk-in customer.";
        ClosePanel();
    }

    // ---- F9 Pay (P1-T10 completes the tender dialog; this pays the exact cash amount) -----------

    [RelayCommand]
    public async Task PayAsync(CancellationToken cancellationToken)
    {
        if (Busy || _bill.Count == 0)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var session = await _sessions.GetCurrentAsync(cancellationToken).ConfigureAwait(true);
                if (session is null)
                {
                    Status = "There is no open shift. Open one before trading.";
                    return;
                }

                var quote = await _quoter.QuoteAsync(_bill, _pendingBillDiscount, cancellationToken).ConfigureAwait(true);

                var completed = await _sales.CompleteAsync(
                    new CompleteSaleCommand(
                        session.UserId,
                        session.ShiftId,
                        _timeProvider.GetLocalNow(),
                        [.. _bill],
                        [new TenderRequest(TenderTypes.Cash, quote.Total)],
                        _customerId,
                        _pendingBillDiscount),
                    cancellationToken).ConfigureAwait(true);

                ClearBillState();
                Status = "Saved as " + completed.BillNo + ". The receipt is queued.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    // ---- F10 Reprint (P1-T11) ----------------------------------------------------------------------

    [RelayCommand]
    public void Reprint() => Status = "Reprint is not available yet - it needs the receipt and print-queue task (P1-T11).";

    // ---- F11 Stock enquiry (SRS FR-4, FR-3.11) ------------------------------------------------------

    [RelayCommand]
    public async Task StockAsync()
    {
        TogglePanel(SalesPanel.StockEnquiry);

        if (!IsStockPanelOpen)
        {
            return;
        }

        var variantId = SelectedLine?.ProductVariantId ?? _lastScannedVariantId;

        if (variantId is null)
        {
            StockEnquiryText = "Scan or select an item first.";
            return;
        }

        await RunAsync(async () =>
        {
            var result = await _stockEnquiry.FindByVariantIdAsync(variantId.Value, CancellationToken.None)
                .ConfigureAwait(true);

            StockEnquiryText = result is null
                ? "That item was not found."
                : BuildStockEnquiryText(result);
        }, CancellationToken.None).ConfigureAwait(true);
    }

    private static string BuildStockEnquiryText(StockEnquiryResult result)
    {
        var lines = new List<string>
        {
            result.Description + " (" + result.Sku + ")",
            "On hand: " + result.QtyBase.ToString("0.###", CultureInfo.InvariantCulture) + " " + result.BaseUomSymbol,
        };

        foreach (var unit in result.AlternateUnits)
        {
            lines.Add("  = " + unit.Quantity.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit.Symbol);
        }

        if (result.CostAvg is { } cost)
        {
            lines.Add("Average cost: " + cost.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return string.Join('\n', lines);
    }

    // ---- F12 Day close (Phase 3) -----------------------------------------------------------------

    [RelayCommand]
    public void DayClose() => Status = "Day close is not available yet - it needs shift management and the Z report (Phase 3).";

    // ---- Esc Cancel ---------------------------------------------------------------------------

    [RelayCommand]
    public void Cancel()
    {
        if (_pendingClearConfirmation)
        {
            _pendingClearConfirmation = false;
            Status = "Cancelled.";
            return;
        }

        if (IsAnyPanelOpen)
        {
            ClosePanel();
            Status = "Cancelled.";
            return;
        }

        Barcode = string.Empty;
    }

    // ---- Open item (SRS FR-2.8) - a button, not one of the reserved UI-02 keys --------------------

    [RelayCommand]
    public async Task OpenItemAsync()
    {
        TogglePanel(SalesPanel.OpenItem);

        if (!IsOpenItemPanelOpen)
        {
            return;
        }

        OpenItemDescription = string.Empty;
        OpenItemPriceText = string.Empty;
        OpenItemQuantityText = "1";

        await RunAsync(async () =>
        {
            var units = await _uoms.ListAsync(CancellationToken.None).ConfigureAwait(true);

            _openItemUnitIds = units.Where(u => u.Active).ToDictionary(u => u.Symbol, u => u.Id, StringComparer.Ordinal);
            OpenItemUnitChoices.Clear();
            foreach (var symbol in _openItemUnitIds.Keys)
            {
                OpenItemUnitChoices.Add(symbol);
            }

            OpenItemSelectedUnit = OpenItemUnitChoices.FirstOrDefault() ?? string.Empty;
        }, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task AddOpenItemAsync()
    {
        var description = OpenItemDescription.Trim();
        if (description.Length == 0)
        {
            Status = "An open item needs a description.";
            return;
        }

        if (!_openItemUnitIds.TryGetValue(OpenItemSelectedUnit, out var uomId))
        {
            Status = "Choose a unit for the open item.";
            return;
        }

        var price = SettingsTextToDecimal(OpenItemPriceText);
        var quantity = SettingsTextToDecimal(OpenItemQuantityText, 1m);

        if (quantity <= 0m)
        {
            Status = "An open item needs a quantity above zero.";
            return;
        }

        await RunAsync(
            () => MutateAndRefreshAsync(
                () =>
                {
                    _bill.Add(new SaleLineRequest(null, quantity, uomId, description, Money.FromDecimal(price)));
                    _lineUnitOptions.Add([]);
                },
                description + " added as an open item.",
                CancellationToken.None),
            CancellationToken.None).ConfigureAwait(true);
        ClosePanel();
    }

    // ---- Scanning (SRS FR-3.1, FR-3.2) -------------------------------------------------------------

    [RelayCommand]
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        var code = Barcode.Trim();
        if (code.Length == 0 || Busy)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var item = await _scanner.ScanAsync(code, cancellationToken).ConfigureAwait(true);
                if (item is null)
                {
                    Status = "No item found for " + code + ".";
                    Barcode = string.Empty;
                    return;
                }

                Barcode = string.Empty;
                await AddOrIncrementAsync(item, cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Adds a freshly scanned or picked item, or - when the same code was scanned again and the
    /// shop's policy asks for it - increases the matching line's quantity instead of adding a
    /// second one (SRS FR-3.2, configurable via <c>PolicySettings.CombineRepeatScans</c>).
    /// </summary>
    private async Task AddOrIncrementAsync(ScannedItem item, CancellationToken cancellationToken)
    {
        _lastScannedVariantId = item.ProductVariantId;

        var existingIndex = _settings.Current.Policy.CombineRepeatScans
            ? _bill.FindIndex(line =>
                line.ProductVariantId == item.ProductVariantId && (line.UomId ?? item.UomId) == item.UomId)
            : -1;

        // Set first, not after: RefreshAsync overwrites this with a negative-stock warning (SRS
        // FR-3.13) when there is one to show, and that is the more important sentence.
        var message = item.Description + (existingIndex >= 0 ? " - quantity increased." : " added.");

        await MutateAndRefreshAsync(
            () =>
            {
                if (existingIndex >= 0)
                {
                    _bill[existingIndex] = _bill[existingIndex] with { Quantity = _bill[existingIndex].Quantity + 1m };
                }
                else
                {
                    _bill.Add(new SaleLineRequest(item.ProductVariantId, 1m));
                    _lineUnitOptions.Add(item.UnitOptions);
                }
            },
            message,
            cancellationToken).ConfigureAwait(true);
    }

    // ---- Line edits (SRS FR-3.5, FR-2.5, FR-3.7) ----------------------------------------------------

    private void OnLineQuantityChanged(int index, decimal quantity)
    {
        if (index < 0 || index >= _bill.Count)
        {
            return;
        }

        _ = RunAsync(
            () => MutateAndRefreshAsync(() => _bill[index] = _bill[index] with { Quantity = quantity }, null, CancellationToken.None),
            CancellationToken.None);
    }

    private void OnLineUnitChanged(int index, long uomId)
    {
        if (index < 0 || index >= _bill.Count)
        {
            return;
        }

        _ = RunAsync(
            () => MutateAndRefreshAsync(() => _bill[index] = _bill[index] with { UomId = uomId }, null, CancellationToken.None),
            CancellationToken.None);
    }

    private void OnLineRemove(int index)
    {
        if (index < 0 || index >= _bill.Count)
        {
            return;
        }

        if (_bill.Count == 1)
        {
            ClearBillState();
            return;
        }

        _ = RunAsync(
            () => MutateAndRefreshAsync(
                () =>
                {
                    _bill.RemoveAt(index);
                    _lineUnitOptions.RemoveAt(index);
                },
                null,
                CancellationToken.None),
            CancellationToken.None);
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to the bill and re-quotes it, or - when the Application
    /// layer refuses the result (an over-cap discount, a blocked negative-stock line, a quantity
    /// a <c>STANDARD</c> product cannot take) - puts the bill back exactly as it was before
    /// <paramref name="mutate"/> ran, so what is on screen never disagrees with what would
    /// actually be charged.
    /// </summary>
    private async Task MutateAndRefreshAsync(Action mutate, string? successMessage, CancellationToken cancellationToken)
    {
        var billBefore = new List<SaleLineRequest>(_bill);
        var unitOptionsBefore = new List<IReadOnlyList<ScannedItemUnitOption>>(_lineUnitOptions);
        var billDiscountBefore = _pendingBillDiscount;

        mutate();

        try
        {
            if (successMessage is not null)
            {
                Status = successMessage;
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            _bill.Clear();
            _bill.AddRange(billBefore);
            _lineUnitOptions.Clear();
            _lineUnitOptions.AddRange(unitOptionsBefore);
            _pendingBillDiscount = billDiscountBefore;
            throw;
        }
    }

    // ---- Refresh, hold/recall payloads, panel plumbing -----------------------------------------------

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var quote = await _quoter.QuoteAsync(_bill, _pendingBillDiscount, cancellationToken).ConfigureAwait(true);

        var selectedIndex = SelectedLine?.Index;

        Lines.Clear();
        for (var i = 0; i < quote.Lines.Count; i++)
        {
            var unitOptions = i < _lineUnitOptions.Count ? _lineUnitOptions[i] : [];
            Lines.Add(new SaleLineViewModel(
                i,
                quote.Lines[i],
                unitOptions,
                OnLineQuantityChanged,
                OnLineUnitChanged,
                OnLineRemove));
        }

        SelectedLine = selectedIndex is { } index && index < Lines.Count ? Lines[index] : null;

        SubtotalText = quote.Subtotal.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        BillDiscountText = quote.BillDiscount.IsZero
            ? string.Empty
            : "-" + quote.BillDiscount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        TaxText = quote.Tax.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        Total = quote.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture);

        if (quote.Warnings.Count > 0)
        {
            Status = string.Join(' ', quote.Warnings);
        }
    }

    partial void OnSelectedLineChanged(SaleLineViewModel? value) => OnPropertyChanged(nameof(DiscountTargetText));

    private HeldBillPayload BuildHeldBillPayload() => new(
        _customerId,
        _pendingBillDiscount?.IsRate ?? false,
        DiscountValue(_pendingBillDiscount),
        [.. _bill.Select(line => new HeldBillLinePayload(
            line.ProductVariantId,
            line.Quantity,
            line.UomId,
            line.OpenItemDescription,
            line.OpenItemUnitPrice?.Amount,
            line.Discount?.IsRate ?? false,
            DiscountValue(line.Discount)))]);

    private async Task RestoreFromPayloadAsync(HeldBillPayload payload, CancellationToken cancellationToken)
    {
        _bill.Clear();
        _lineUnitOptions.Clear();

        _customerId = payload.CustomerId;
        _customerName = payload.CustomerId is { } customerId
            ? (await _customers.FindByIdAsync(customerId, cancellationToken).ConfigureAwait(true))?.Name
            : null;
        OnPropertyChanged(nameof(CurrentCustomerText));

        _pendingBillDiscount = ToDiscountInput(payload.BillDiscountIsRate, payload.BillDiscountValue);

        foreach (var linePayload in payload.Lines)
        {
            var discount = ToDiscountInput(linePayload.DiscountIsRate, linePayload.DiscountValue);

            _bill.Add(new SaleLineRequest(
                linePayload.ProductVariantId,
                linePayload.Quantity,
                linePayload.UomId,
                linePayload.OpenItemDescription,
                linePayload.OpenItemUnitPrice is { } price ? Money.FromDecimal(price) : null,
                discount));

            if (linePayload.ProductVariantId is { } variantId)
            {
                var item = await _scanner.ScanByVariantIdAsync(variantId, cancellationToken).ConfigureAwait(true);
                _lineUnitOptions.Add(item?.UnitOptions ?? []);
            }
            else
            {
                _lineUnitOptions.Add([]);
            }
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private static decimal? DiscountValue(DiscountInput? discount) => discount is { } value
        ? (value.IsRate ? value.Rate.Fraction : value.Amount.Amount)
        : null;

    private static DiscountInput? ToDiscountInput(bool isRate, decimal? value) => value is { } v
        ? (isRate ? DiscountInput.OfRate(Percentage.FromFraction(v)) : DiscountInput.OfAmount(Money.FromDecimal(v)))
        : null;

    private void ClearBillState()
    {
        _bill.Clear();
        _lineUnitOptions.Clear();
        _customerId = null;
        _customerName = null;
        _pendingBillDiscount = null;
        _lastScannedVariantId = null;
        SelectedLine = null;
        Lines.Clear();
        SubtotalText = "0.00";
        BillDiscountText = string.Empty;
        TaxText = "0.00";
        Total = "0.00";
        ClosePanel();
        OnPropertyChanged(nameof(CurrentCustomerText));
    }

    private void TogglePanel(SalesPanel panel)
    {
        _activePanel = _activePanel == panel ? SalesPanel.None : panel;
        RaisePanelChanged();
    }

    private void ClosePanel()
    {
        _activePanel = SalesPanel.None;
        RaisePanelChanged();
    }

    private void RaisePanelChanged()
    {
        OnPropertyChanged(nameof(IsHelpPanelOpen));
        OnPropertyChanged(nameof(IsSearchPanelOpen));
        OnPropertyChanged(nameof(IsHoldPanelOpen));
        OnPropertyChanged(nameof(IsHeldBillsPanelOpen));
        OnPropertyChanged(nameof(IsDiscountPanelOpen));
        OnPropertyChanged(nameof(IsCustomerPanelOpen));
        OnPropertyChanged(nameof(IsStockPanelOpen));
        OnPropertyChanged(nameof(IsOpenItemPanelOpen));
        OnPropertyChanged(nameof(IsAnyPanelOpen));
        OnPropertyChanged(nameof(DiscountTargetText));
    }

    private static decimal SettingsTextToDecimal(string? text, decimal fallback = 0m) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>
    /// Runs an Application call with the screen locked, and turns anything that goes wrong into
    /// a sentence the cashier can act on (SRS UI-06).
    /// </summary>
    private async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        Busy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            // The Application layer said no, in plain language. Show it; do not interpret it.
            Status = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        finally
        {
            Busy = false;
        }
    }
}
