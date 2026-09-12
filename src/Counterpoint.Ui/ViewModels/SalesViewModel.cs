using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Sales;
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
    Payment,
    OpenShift,
    Dashboard,
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
/// <b>F9 Pay</b> (P1-T10, SRS FR-3.16-FR-3.22, FR-3.24-FR-3.26) opens a non-modal payment panel
/// pre-filled with a single cash tender for the exact total, so the one-tender fast path is still
/// F9 then Enter. The cashier can change that amount (a bigger cash figure previews the change
/// due) or type amounts into card, bank transfer and cheque as well, splitting the bill across
/// as many of the four as needed; <c>Counterpoint.Domain.Sales.TenderCalculator</c> - the same
/// calculator the Application layer re-checks the bill against before it is written - is what
/// turns those figures into the change preview, so the number shown here is never a guess.
/// </para>
/// <para>
/// <b>Deliberately out of this task's reach</b> (see the task's own notes and
/// <c>docs/03_PHASE_1_core_trading.md</c>): F4 Return (Phase 2), F12 Day Close (needs Phase 3's
/// shift close), and an owner re-authentication dialog for a discount above its cap (SRS FR-3.18)
/// or a manual price override (FR-3.19) - nothing in Phase 1 builds that dialog yet, so an
/// over-cap discount is refused with a plain-language message rather than offered an override.
/// Trade price tiers (FR-3.23) are Phase 5's, explicitly out of scope for Phase 1
/// (docs/03_PHASE_1_core_trading.md); attaching a customer here (F8) is for record-keeping only
/// and does not change a line's price. Bill cancellation (SRS FR-3.34) is
/// <c>Counterpoint.Application.Sales.ICancelSale</c>, complete and directly testable in this
/// task - it has no trigger on this screen because cancelling needs to find a past bill first
/// (SRS FR-3.35), and no bill-lookup screen exists yet in Phase 1.
/// </para>
/// <para>
/// <b>F10 Reprint</b> (P1-T11, SRS FR-3.36, FR-7.5, FR-7.6) reprints the last bill completed on
/// this till - the fast path a cashier actually uses. Reprinting any past bill by number needs
/// the same bill-lookup screen cancellation is waiting on; <c>IReprintReceipt.ReprintAsync</c>
/// itself already accepts any sale id, so that screen only has to find the id.
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
    private readonly IOpenShift _openShift;
    private readonly IDashboardQueries _dashboard;
    private readonly IReprintReceipt _reprints;
    private readonly IPrintJobOutbox _printJobs;
    private readonly TimeProvider _timeProvider;

    private readonly List<SaleLineRequest> _bill = [];
    private readonly List<IReadOnlyList<ScannedItemUnitOption>> _lineUnitOptions = [];

    private long? _customerId;
    private string? _customerName;
    private long? _lastCompletedSaleId;
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

    // ---- Payment panel (F9, SRS FR-3.16-FR-3.22, FR-3.24-FR-3.26) ---------------------------------

    private string _cashTenderText = string.Empty;

    /// <summary>
    /// What the cashier typed for cash. Pre-filled with the exact total when the panel opens.
    /// Setting it re-runs <see cref="RefreshTenderPreview"/> - these four boxes have no source
    /// generator behind them (<see cref="SetNumeric"/> needs a settable field, not the
    /// <c>[ObservableProperty]</c> pattern the rest of the screen uses), so the preview is kept
    /// live by hand rather than through a generated <c>OnXChanged</c> partial method.
    /// </summary>
    public string CashTenderText
    {
        get => _cashTenderText;
        set
        {
            SetNumeric(ref _cashTenderText, value, allowDecimal: true);
            RefreshTenderPreview();
        }
    }

    private string _cardTenderText = string.Empty;

    public string CardTenderText
    {
        get => _cardTenderText;
        set
        {
            SetNumeric(ref _cardTenderText, value, allowDecimal: true);
            RefreshTenderPreview();
        }
    }

    private string _bankTransferTenderText = string.Empty;

    public string BankTransferTenderText
    {
        get => _bankTransferTenderText;
        set
        {
            SetNumeric(ref _bankTransferTenderText, value, allowDecimal: true);
            RefreshTenderPreview();
        }
    }

    private string _chequeTenderText = string.Empty;

    public string ChequeTenderText
    {
        get => _chequeTenderText;
        set
        {
            SetNumeric(ref _chequeTenderText, value, allowDecimal: true);
            RefreshTenderPreview();
        }
    }

    /// <summary>
    /// The live change/still-owed preview (SRS FR-3.26), recomputed from
    /// <c>Counterpoint.Domain.Sales.TenderCalculator</c> on every keystroke in any of the four
    /// tender boxes above - the same calculator the Application layer re-checks the bill against,
    /// so this is never a number the bill can then disagree with.
    /// </summary>
    [ObservableProperty]
    private string _tenderPreviewText = string.Empty;

    /// <summary>
    /// Quick-tender buttons for common note denominations (SRS FR-3.26). LKR notes (Q-01); there
    /// is no FR-10 setting for this list to read instead (P1-T03's settings framework has none),
    /// so it is fixed here rather than inventing one this task does not need.
    /// </summary>
    public IReadOnlyList<decimal> QuickCashDenominations { get; } = [20m, 50m, 100m, 500m, 1000m, 5000m];

    // ---- Open shift panel (SRS FR-8.1) -------------------------------------------------------------

    private string _openingFloatText = string.Empty;

    /// <summary>The cash counted into the drawer before trading starts, typed by the cashier.</summary>
    public string OpeningFloatText
    {
        get => _openingFloatText;
        set => SetNumeric(ref _openingFloatText, value, allowDecimal: true);
    }

    /// <summary>Whether the till has no open shift to trade in - the button that opens this panel is only shown then.</summary>
    public bool CanOpenShift => _session.ShiftId is null;

    // ---- Dashboard panel (SRS FR-9.7) ---------------------------------------------------------------

    [ObservableProperty]
    private string _dashboardText = string.Empty;

    /// <summary>
    /// The last backup <see cref="RefreshDashboardAsync"/> read, or null before its first tick or
    /// on a till that has never taken one. Backs <see cref="StatusBackupText"/>, which is always
    /// visible, unlike <see cref="DashboardText"/> above it.
    /// </summary>
    private LastBackupStatus? _lastBackup;

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
        F9  Pay                  - cash, card, bank transfer or cheque, split as needed; Enter pays
        F10 Reprint               - print the last bill again, marked DUPLICATE
        F11 Stock                 - stock on hand for the selected or last-scanned item
        F12 Day close              - not available yet (Phase 3)
        Esc Cancel                  - close whatever panel is open, or clear the scan box
        Open shift                  - shown only when no shift is open; enter the cash float and start trading
        Dashboard                    - today's sales, bill count, average bill, cash in drawer, low stock, last backup
        """;

    // ---- Status bar (SRS UI-09) --------------------------------------------------------------------

    public string StatusUserText => _session.CurrentUser is { } user
        ? user.DisplayName + " (" + (user.Role == Role.Owner ? "owner" : "cashier") + ")"
        : "Not signed in";

    public string StatusShiftText => _session.ShiftId is { } shiftId
        ? "Shift #" + shiftId.ToString(CultureInfo.InvariantCulture)
        : "No shift open";

    /// <summary>
    /// The status bar's permanent last-backup indicator (SRS FR-9.7, FR-11.7, P1-T15), with the
    /// same escalating warning <see cref="DashboardText"/>'s "Last backup" line shows. Filled by
    /// <see cref="RefreshDashboardAsync"/>, which <c>SalesWindow</c>'s low-frequency timer calls
    /// whether or not the dashboard panel is open.
    /// </summary>
    public string StatusBackupText => "Backup: " + BuildLastBackupText(
        _lastBackup, _settings.Current.Backup.WarnAfterDays, _timeProvider.GetUtcNow());

    public string StatusCloudText => "Cloud: " +
        (_settings.Current.Backup.CloudTarget == CloudBackupTarget.None ? "off" : _settings.Current.Backup.CloudTarget.ToString());

    public string StatusPrinterText => string.IsNullOrWhiteSpace(_settings.Current.Peripherals.ReceiptPrinterName)
        ? "Printer: file (development)"
        : "Printer: " + _settings.Current.Peripherals.ReceiptPrinterName;

    // ---- Closing with a shift open (SRS FR-8.7) ------------------------------------------------
    // FR-8.7 is two halves: recover an open shift cleanly on restart (ITillSessionProvider,
    // AuthenticationService.LogInAsync - already in place before this) and warn if the app is
    // closed with a shift open (this). The recovery path already makes closing with a shift open
    // safe, so this is a plain-language warning (SRS UI-06), never a hard stop - SalesWindow's
    // code-behind reads this on Window.Closing and decides what to do with it; nothing here
    // refuses or delays anything itself, keeping the decision testable without a real window.

    /// <summary>
    /// The sentence to show a cashier who is about to close the app with a shift still open, or
    /// null when there is nothing to warn about.
    /// </summary>
    public string? ShutdownWarning => _session.ShiftId is null
        ? null
        : "A shift is still open. Closing now is fine - it will recover automatically next time "
            + "you sign in - but make sure this is intentional. Close again to confirm.";

    /// <summary>
    /// "2 pending, 1 failed" or "Print queue empty" - the status-bar indicator P1-T11 asks for.
    /// Refreshed after anything that queues or retries a print job; it is not a live poll, the
    /// same trade-off <see cref="StatusBackupText"/> makes for now.
    /// </summary>
    [ObservableProperty]
    private string _statusPrintQueueText = "Print queue: -";

    /// <summary>Raised when the cashier asks for the print queue screen (P1-T11).</summary>
    public event EventHandler? PrintQueueRequested;

    [RelayCommand]
    public void OpenPrintQueue() => PrintQueueRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Re-reads the print queue and updates <see cref="StatusPrintQueueText"/>.</summary>
    private async Task RefreshPrintQueueStatusAsync(CancellationToken cancellationToken)
    {
        var jobs = await _printJobs.ListQueueAsync(cancellationToken).ConfigureAwait(true);
        var pending = jobs.Count(job => job.Status == "PENDING");
        var failed = jobs.Count(job => job.Status == "FAILED");

        StatusPrintQueueText = pending == 0 && failed == 0
            ? "Print queue empty"
            : "Print queue: " + pending.ToString(CultureInfo.InvariantCulture) + " pending, "
                + failed.ToString(CultureInfo.InvariantCulture) + " failed";
    }

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
        IOpenShift openShift,
        IDashboardQueries dashboard,
        IReprintReceipt reprints,
        IPrintJobOutbox printJobs,
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
        ArgumentNullException.ThrowIfNull(openShift);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(reprints);
        ArgumentNullException.ThrowIfNull(printJobs);
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
        _openShift = openShift;
        _dashboard = dashboard;
        _reprints = reprints;
        _printJobs = printJobs;
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

    /// <summary>Raised when the owner asks for the purchase-order screen (SRS FR-4.5, FR-4.6, FR-4.10).</summary>
    public event EventHandler? PurchaseOrdersRequested;

    /// <summary>The lines on the bill, in the order they were scanned.</summary>
    public ObservableCollection<SaleLineViewModel> Lines { get; } = [];

    public bool CanManageUsers => _session.CurrentUser?.Role == Role.Owner;

    public bool CanChangeSettings => _session.CurrentUser?.Role == Role.Owner;

    public bool CanManageCatalogue => _session.CurrentUser?.Role == Role.Owner;

    public bool CanPrintLabels => _session.CurrentUser?.Role == Role.Owner;

    /// <summary>Purchasing is an owner capability (SRS §3.3 ROLE-2, task P2-T06).</summary>
    public bool CanManagePurchasing => _session.CurrentUser?.Role == Role.Owner;

    public bool IsHelpPanelOpen => _activePanel == SalesPanel.Help;

    public bool IsSearchPanelOpen => _activePanel == SalesPanel.Search;

    public bool IsHoldPanelOpen => _activePanel == SalesPanel.Hold;

    public bool IsHeldBillsPanelOpen => _activePanel == SalesPanel.HeldBills;

    public bool IsDiscountPanelOpen => _activePanel == SalesPanel.Discount;

    public bool IsCustomerPanelOpen => _activePanel == SalesPanel.Customer;

    public bool IsStockPanelOpen => _activePanel == SalesPanel.StockEnquiry;

    public bool IsOpenItemPanelOpen => _activePanel == SalesPanel.OpenItem;

    public bool IsPaymentPanelOpen => _activePanel == SalesPanel.Payment;

    public bool IsOpenShiftPanelOpen => _activePanel == SalesPanel.OpenShift;

    public bool IsDashboardPanelOpen => _activePanel == SalesPanel.Dashboard;

    public bool IsAnyPanelOpen => _activePanel != SalesPanel.None;

    [RelayCommand]
    public void ManageUsers() => ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManageCatalogue() => CatalogueRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void PrintLabels() => LabelPrintRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManagePurchaseOrders() => PurchaseOrdersRequested?.Invoke(this, EventArgs.Empty);

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

    // ---- F9 Pay (P1-T10, SRS FR-3.16-FR-3.22, FR-3.24-FR-3.26) -----------------------------------

    /// <summary>
    /// Opens the payment panel, pre-filled with the exact total as a single cash tender - so the
    /// one-tender fast path is still F9 then Enter, exactly as it was before this task, and a
    /// second press of F9 (<see cref="TogglePanel"/>) closes it again without paying, the same as
    /// every other panel.
    /// </summary>
    [RelayCommand]
    public void Pay()
    {
        if (_bill.Count == 0)
        {
            Status = "There is nothing on the bill to pay for.";
            return;
        }

        TogglePanel(SalesPanel.Payment);

        if (!IsPaymentPanelOpen)
        {
            return;
        }

        CashTenderText = Total;
        CardTenderText = string.Empty;
        BankTransferTenderText = string.Empty;
        ChequeTenderText = string.Empty;
        RefreshTenderPreview();
    }

    /// <summary>Adds one note to the cash tender box (SRS FR-3.26's quick-tender buttons).</summary>
    [RelayCommand]
    public void AddQuickCash(decimal denomination)
    {
        var running = SettingsTextToDecimal(CashTenderText) + denomination;
        CashTenderText = running.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Recomputes the change/still-owed preview from whatever is currently typed, through the
    /// same <c>Counterpoint.Domain.Sales.TenderCalculator</c> the Application layer checks the
    /// bill against - never a separate calculation of its own (see the class remarks).
    /// </summary>
    private void RefreshTenderPreview()
    {
        var tenders = BuildTenderLines();

        if (tenders.Count == 0)
        {
            TenderPreviewText = "Enter at least one amount tendered.";
            return;
        }

        try
        {
            var plan = TenderCalculator.Calculate(Money.FromDecimal(SettingsTextToDecimal(Total)), tenders);
            TenderPreviewText = plan.Change.IsZero
                ? "Paid in full."
                : "Change due: " + plan.Change.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException exception)
        {
            TenderPreviewText = exception.Message;
        }
    }

    private List<TenderLine> BuildTenderLines()
    {
        var lines = new List<TenderLine>(4);
        AddTenderLine(lines, TenderTypes.Cash, CashTenderText);
        AddTenderLine(lines, TenderTypes.Card, CardTenderText);
        AddTenderLine(lines, TenderTypes.BankTransfer, BankTransferTenderText);
        AddTenderLine(lines, TenderTypes.Cheque, ChequeTenderText);
        return lines;
    }

    private static void AddTenderLine(List<TenderLine> lines, string tenderType, string typed)
    {
        var amount = SettingsTextToDecimal(typed);
        if (amount > 0m)
        {
            lines.Add(new TenderLine(tenderType, Money.FromDecimal(amount)));
        }
    }

    /// <summary>
    /// Completes the sale with whatever is typed into the payment panel - the Application layer
    /// is the authority on whether it adds up (SRS FR-3.30); this only builds the request and
    /// shows what comes back, exactly the discipline every other command on this screen keeps.
    /// </summary>
    [RelayCommand]
    public async Task CompletePaymentAsync(CancellationToken cancellationToken)
    {
        if (Busy || _bill.Count == 0)
        {
            return;
        }

        var tenders = BuildTenderLines();
        if (tenders.Count == 0)
        {
            Status = "Enter at least one amount tendered.";
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

                var completed = await _sales.CompleteAsync(
                    new CompleteSaleCommand(
                        session.UserId,
                        session.ShiftId,
                        _timeProvider.GetLocalNow(),
                        [.. _bill],
                        [.. tenders.Select(tender => new TenderRequest(tender.TenderType, tender.Tendered, tender.Reference))],
                        _customerId,
                        _pendingBillDiscount),
                    cancellationToken).ConfigureAwait(true);

                ClearBillState();
                _lastCompletedSaleId = completed.SaleId;
                Status = completed.Change.IsZero
                    ? "Saved as " + completed.BillNo + ". The receipt is queued."
                    : "Saved as " + completed.BillNo + ". Change due: "
                        + completed.Change.Amount.ToString("0.00", CultureInfo.InvariantCulture)
                        + ". The receipt is queued.";

                await RefreshPrintQueueStatusAsync(cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    // ---- F10 Reprint (P1-T11) ----------------------------------------------------------------------

    /// <summary>
    /// Reprints the last bill completed on this till, marked DUPLICATE and logged
    /// (SRS FR-3.36, FR-7.5, FR-7.6). Any signed-in cashier may reprint (SRS §3.3 ROLE-1).
    /// </summary>
    /// <remarks>
    /// Reprinting <em>any</em> past bill by number needs a bill-lookup screen this phase does not
    /// build yet - the same gap <c>CancelSaleHandler</c>'s own remarks note for cancellation. F10
    /// covers the fast path a cashier actually reaches for: "print that last one again."
    /// </remarks>
    [RelayCommand]
    public async Task ReprintAsync(CancellationToken cancellationToken)
    {
        if (Busy)
        {
            return;
        }

        if (_lastCompletedSaleId is not { } saleId)
        {
            Status = "Nothing has been completed on this till yet.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var reprinted = await _reprints.ReprintAsync(saleId, cancellationToken).ConfigureAwait(true);
                Status = "Reprinted " + reprinted.BillNo + ", marked DUPLICATE.";

                await RefreshPrintQueueStatusAsync(cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

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

    // ---- Open shift (SRS FR-8.1) - a button, not one of the reserved UI-02 keys --------------------
    // A sale cannot be made without an open shift (SRS FR-8.1, C-01) - CompletePaymentAsync above
    // already refuses one when ITillSessionProvider finds none open. This is what lets the
    // cashier get out of that state without restarting the till.

    [RelayCommand]
    public void OpenShift()
    {
        if (!CanOpenShift)
        {
            return;
        }

        OpeningFloatText = string.Empty;
        TogglePanel(SalesPanel.OpenShift);
    }

    [RelayCommand]
    public async Task ConfirmOpenShiftAsync(CancellationToken cancellationToken)
    {
        if (_session.CurrentUser is not { } user)
        {
            Status = "Nobody is signed in.";
            return;
        }

        var amount = SettingsTextToDecimal(OpeningFloatText);

        await RunAsync(
            async () =>
            {
                var opened = await _openShift.OpenAsync(
                    new OpenShiftCommand(user.Id, Money.FromDecimal(amount), _timeProvider.GetLocalNow()),
                    cancellationToken).ConfigureAwait(true);

                ClosePanel();
                Status = "Opened shift " + opened.ShiftNo + ".";

                // Session.ShiftId already changed (OpenShiftHandler set it inside the same call),
                // but nothing on this screen is bound to it through change notification, so the
                // status bar and this button's visibility are told by hand, the same as every
                // other figure this viewmodel computes from a service it does not own.
                OnPropertyChanged(nameof(StatusShiftText));
                OnPropertyChanged(nameof(CanOpenShift));

                await RefreshDashboardAsync(cancellationToken).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    // ---- Dashboard (SRS FR-9.7) - a button, not one of the reserved UI-02 keys --------------------
    // Refreshed when the panel opens and on SalesWindow's low-frequency timer while it stays open
    // (P1-T14 "Risks": scoped to today and refreshed on a timer, never on every keystroke, so it
    // cannot compete with the scan path for the single write connection - this is a read-only
    // query on a read connection either way).

    [RelayCommand]
    public async Task DashboardAsync(CancellationToken cancellationToken)
    {
        TogglePanel(SalesPanel.Dashboard);

        if (IsDashboardPanelOpen)
        {
            await RefreshDashboardAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Reloads <see cref="DashboardText"/> and <see cref="StatusBackupText"/> from
    /// <see cref="IDashboardQueries"/>. <c>SalesWindow</c>'s timer calls this unconditionally, on
    /// every tick, whether or not the dashboard panel is open - the status bar's backup indicator
    /// is permanent (SRS FR-9.7, FR-11.7), even though <see cref="DashboardText"/> itself is only
    /// worth recomputing while somebody is looking at the panel it fills.
    /// </summary>
    /// <remarks>
    /// Deliberately not routed through <see cref="RunAsync"/>: this never sets <see cref="Busy"/>
    /// (a background dashboard tick must not grey out F9 Pay while a bill is being built) and it
    /// never writes to <see cref="Status"/> (a failed background refresh is not something worth
    /// interrupting the cashier's screen to report). Caught and quietly skipped instead - the
    /// same "never block the sale" spirit as CLAUDE.md invariant 7, applied to a screen refresh
    /// that has no business holding up or announcing itself over anything else on this screen.
    /// </remarks>
    [RelayCommand]
    public async Task RefreshDashboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _dashboard.GetSummaryAsync(cancellationToken).ConfigureAwait(true);

            _lastBackup = summary.LastBackup;
            OnPropertyChanged(nameof(StatusBackupText));

            if (IsDashboardPanelOpen)
            {
                DashboardText = BuildDashboardText(summary, _settings.Current.Backup.WarnAfterDays, _timeProvider.GetUtcNow());
            }
        }
        catch (InvalidOperationException)
        {
            // Left showing whatever it last showed - a stale figure is a better background-tick
            // failure than a blank panel or an unobserved exception.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string BuildDashboardText(DashboardSummary summary, int warnAfterDays, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            "Today's sales: " + summary.TodaysSales.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            "Bills: " + summary.BillCount.ToString(CultureInfo.InvariantCulture),
            "Average bill: " + summary.AverageBill.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            "Cash in drawer: " + (summary.CashInDrawer is { } cash
                ? cash.Amount.ToString("0.00", CultureInfo.InvariantCulture)
                : "no shift open"),
            "Low stock items: " + summary.LowStockCount.ToString(CultureInfo.InvariantCulture),
            "Last backup: " + BuildLastBackupText(summary.LastBackup, warnAfterDays, now),
        };

        return string.Join('\n', lines);
    }

    /// <summary>
    /// "2026-09-08 20:00", or that plus an escalating warning once <paramref name="warnAfterDays"/>
    /// is exceeded (SRS FR-11.7's local half - P1-T15; the cloud figure FR-11.7 also names is
    /// Phase 4's, tracked separately once the uploader exists). Doubling the configured limit
    /// before calling it urgent is this screen's own escalation, not a separate setting: a shop
    /// that has gone twice as long as it said it would tolerate is past "remember to do this
    /// today" and into "something is actually wrong".
    /// </summary>
    private static string BuildLastBackupText(LastBackupStatus? lastBackup, int warnAfterDays, DateTimeOffset now)
    {
        if (lastBackup is null)
        {
            return "none yet - WARNING: no backup has ever been taken.";
        }

        var when = lastBackup.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var daysSince = (now - lastBackup.TakenAt).TotalDays;

        if (daysSince < warnAfterDays)
        {
            return when;
        }

        var wholeDays = (int)Math.Floor(daysSince);
        var severity = daysSince >= warnAfterDays * 2 ? "URGENT" : "WARNING";

        return when + " - " + severity + ": " + wholeDays.ToString(CultureInfo.InvariantCulture)
            + " day(s) since the last backup (the shop's own limit is "
            + warnAfterDays.ToString(CultureInfo.InvariantCulture) + ").";
    }

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

            // Putting the model back is not enough on its own: the screen already re-rendered
            // (or, for a line already on screen, the cashier's own edit already sits in the box)
            // before the Application layer's refusal came back. Re-quote the reverted bill so
            // every line's displayed quantity, unit and total goes back with it - otherwise a
            // rejected edit leaves the box showing the very value that was just refused, which
            // is exactly the disagreement between screen and charge this method exists to
            // prevent. Any failure re-quoting a bill that priced cleanly a moment ago would be
            // a second, unrelated bug; it must not hide the first one, so it is swallowed here.
            try
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                // Deliberately ignored - see remarks above.
            }

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
        CashTenderText = string.Empty;
        CardTenderText = string.Empty;
        BankTransferTenderText = string.Empty;
        ChequeTenderText = string.Empty;
        TenderPreviewText = string.Empty;
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
        OnPropertyChanged(nameof(IsPaymentPanelOpen));
        OnPropertyChanged(nameof(IsOpenShiftPanelOpen));
        OnPropertyChanged(nameof(IsDashboardPanelOpen));
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
