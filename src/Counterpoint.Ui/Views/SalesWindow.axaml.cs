using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Counterpoint.Application.Settings;
using Counterpoint.Ui.Input;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The sales screen. Markup and one behaviour that cannot be a binding: telling a barcode
/// scanner's keystrokes apart from a cashier typing, and routing a scan to the bill regardless
/// of which control has focus (SRS FR-3.2, UI-01, P1-T09 "Do this" #4).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScannerKeystrokeFilter"/> does the classification - it is the part of this a test
/// can drive directly, with no window at all. This class is the thin, Avalonia-facing half: it
/// reads real <c>TextInput</c> and <c>KeyDown</c> events at the <b>window</b>, in the tunnelling
/// phase, before whatever control has focus ever sees them, and only intervenes once the filter
/// has decided a run is a scan.
/// </para>
/// <para>
/// <b>A documented limitation, not a silent one.</b> Because a run cannot be told apart from
/// typing until enough of it has arrived, the first
/// <see cref="ScannerKeystrokeFilter.BufferedLength"/> characters of a scan that happens to start
/// while some other text box has focus do briefly land in that box before this class can prove
/// they were not typing and pull them back out. A fully separate scanner input path - the
/// scanner as its own device rather than a keyboard wedge sharing the OS keyboard stream - is a
/// real-hardware concern for <c>HW-T02</c>, not something the Linux fakes this task is built and
/// accepted against can settle any better than this.
/// </para>
/// </remarks>
public partial class SalesWindow : Window
{
    /// <summary>SRS FR-3.2, UI-01, P1-T09 "Do this" #4: under 30 ms between keystrokes is scanner speed.</summary>
    private static readonly TimeSpan MaxInterKeyGap = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// How long the till waits for another keystroke before it treats a buffered run as finished
    /// when the shop's scanner sends no suffix at all (<see cref="ScannerSuffix.None"/>).
    /// </summary>
    private static readonly TimeSpan IdleCommitDelay = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// How often the dashboard panel re-reads its figures while it is open (SRS FR-9.7, P1-T14
    /// "Risks": a timer, never on every keystroke). Independent of <see cref="_idleTimer"/> and
    /// the scanner path above - a low-frequency background read on its own read connection, which
    /// cannot compete with the single write connection a scan-to-line or a sale is using
    /// (NFR-P1, NFR-P2).
    /// </summary>
    private static readonly TimeSpan DashboardRefreshInterval = TimeSpan.FromSeconds(30);

    private ScannerKeystrokeFilter? _filter;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _dashboardTimer;

    /// <summary>
    /// Whether the shift-open warning (SRS FR-8.7) has already been shown for this close attempt.
    /// Set once the first <see cref="OnClosing"/> cancels the close to show it, so a second close
    /// - the cashier's own confirmation, the same "press again to confirm" idiom
    /// <c>SalesViewModel.NewSale</c> already uses for clearing a bill - goes through unopposed.
    /// This is the warning FR-8.7 asks for, not a hard stop: nothing here can trap a cashier who
    /// closes twice.
    /// </summary>
    private bool _shiftOpenWarningShown;

    public SalesWindow()
    {
        InitializeComponent();

        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // SRS FR-3.1: "the cursor placed in the scan/search field by default."
        Opened += (_, _) => ScanBox.Focus();

        Opened += (_, _) => StartDashboardTimer();
        Closed += (_, _) => _dashboardTimer?.Stop();

        Closing += OnClosing;
    }

    /// <summary>
    /// SRS FR-8.7's warning half: if a shift is open when the cashier closes the app - the window
    /// X, Alt+F4, a shutdown, however this Avalonia app's close is triggered - cancel the first
    /// attempt, show <see cref="SalesViewModel.ShutdownWarning"/> as the status line, and let a
    /// second close through. The decision itself (<c>ShutdownWarning</c>) lives on the viewmodel
    /// precisely so a test can drive it without a real window; this handler only sequences it.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shiftOpenWarningShown || ViewModel is not { } viewModel || viewModel.ShutdownWarning is not { } warning)
        {
            return;
        }

        e.Cancel = true;
        _shiftOpenWarningShown = true;
        viewModel.Status = warning;
    }

    /// <summary>
    /// Ticks <see cref="SalesViewModel.RefreshDashboardCommand"/> on a low-frequency timer.
    /// <c>RefreshDashboardAsync</c> itself is a no-op while the panel is closed, so this never
    /// reads the database for a figure nobody is looking at, and it never touches anything the
    /// scanner path above depends on.
    /// </summary>
    private void StartDashboardTimer()
    {
        _dashboardTimer ??= new DispatcherTimer { Interval = DashboardRefreshInterval };
        _dashboardTimer.Tick -= OnDashboardTimerTick;
        _dashboardTimer.Tick += OnDashboardTimerTick;
        _dashboardTimer.Start();
    }

    private void OnDashboardTimerTick(object? sender, EventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.RefreshDashboardCommand.CanExecute(null))
        {
            _ = viewModel.RefreshDashboardCommand.ExecuteAsync(null);
        }
    }

    private SalesViewModel? ViewModel => DataContext as SalesViewModel;

    private ScannerKeystrokeFilter Filter =>
        _filter ??= new ScannerKeystrokeFilter(Math.Max(ViewModel?.ScannerMinimumLength ?? 4, 1), MaxInterKeyGap);

    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        var text = e.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var filter = Filter;
        var now = DateTimeOffset.UtcNow;
        var focusedTextBox = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;

        foreach (var character in text)
        {
            filter.Push(character, now);

            if (filter.JustBecameConfirmedScan && focusedTextBox is not null && !ReferenceEquals(focusedTextBox, ScanBox))
            {
                // The run just proved itself a scan. Everything up to and including this
                // keystroke already leaked into whatever box had focus - there was no way to know
                // sooner - so this pulls exactly that many characters back out, once, rather than
                // replaying every keystroke from here on (those are handled below instead).
                StripTrailingCharacters(focusedTextBox, filter.BufferedLength);
            }
        }

        if (filter.IsConfirmedScanInProgress)
        {
            // From the confirming keystroke onward this run belongs to the scan box, not to
            // whatever is focused (P1-T09 "Do this" #4: "scans route to the scan box regardless
            // of focus").
            e.Handled = true;
        }

        RestartIdleTimerIfNoSuffixConfigured();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsConfiguredSuffixKey(e.Key))
        {
            return;
        }

        _idleTimer?.Stop();
        var code = Filter.Commit();

        if (code is null)
        {
            // Not a scan - a cashier typing and then pressing this key normally, which the
            // control that has focus (a KeyBinding on the scan box, a search box, ...) still
            // gets to react to.
            return;
        }

        // A confirmed scan's suffix belongs to the scan, not to whatever this key would
        // otherwise have done on the focused control (a bound Enter, a newline, a tab stop).
        e.Handled = true;
        RouteScan(code);
    }

    private bool IsConfiguredSuffixKey(Key key) => (ViewModel?.ScannerSuffixSetting ?? ScannerSuffix.Enter) switch
    {
        ScannerSuffix.Enter => key == Key.Enter,
        ScannerSuffix.Tab => key == Key.Tab,
        _ => false,
    };

    private void RouteScan(string code)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Barcode = code;
        _ = viewModel.ScanCommand.ExecuteAsync(null);
    }

    private static void StripTrailingCharacters(TextBox textBox, int count)
    {
        var text = textBox.Text;
        if (string.IsNullOrEmpty(text) || count <= 0)
        {
            return;
        }

        var removeFrom = Math.Max(text.Length - count, 0);
        textBox.Text = text[..removeFrom];
        textBox.CaretIndex = textBox.Text.Length;
    }

    /// <summary>
    /// A scanner configured to send no suffix at all (<see cref="ScannerSuffix.None"/>) gives the
    /// till nothing to commit on except silence: if nothing more arrives for
    /// <see cref="IdleCommitDelay"/>, whatever is buffered is as finished as it is going to get.
    /// </summary>
    private void RestartIdleTimerIfNoSuffixConfigured()
    {
        if (ViewModel?.ScannerSuffixSetting != ScannerSuffix.None)
        {
            return;
        }

        if (_idleTimer is null)
        {
            _idleTimer = new DispatcherTimer { Interval = IdleCommitDelay };
            _idleTimer.Tick += OnIdleTimerTick;
        }

        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        _idleTimer?.Stop();

        var code = Filter.Commit();
        if (code is not null)
        {
            RouteScan(code);
        }
    }
}
