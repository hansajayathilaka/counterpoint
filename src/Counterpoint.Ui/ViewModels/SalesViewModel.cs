using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The sales screen: a scan box, the lines on the bill, the total, and Pay.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no business logic here, and there must never be.</b> The viewmodel holds what
/// the cashier has typed, calls three Application interfaces, and shows what comes back.
/// It does not price a line, apply a rounding rule, decide what a cashier may do, or know that
/// SQLite exists (CLAUDE.md invariant 8, SRS NFR-S2, AC-17). Counterpoint.Ui cannot even
/// reference the projects that could tell it - the architecture test sees to that.
/// </para>
/// <para>
/// Deliberately thin, because it is thrown away: the real sales screen, with search, quantity
/// editing, line removal, held bills, discounts and split tender, is P1-T09 and P1-T10.
/// </para>
/// </remarks>
public sealed partial class SalesViewModel : ViewModelBase
{
    private readonly IScanItem _scanner;
    private readonly IQuoteSale _quoter;
    private readonly ICompleteSale _sales;
    private readonly ITillSessionProvider _sessions;
    private readonly TimeProvider _timeProvider;
    private readonly List<SaleLineRequest> _bill = [];

    [ObservableProperty]
    private string _barcode = string.Empty;

    [ObservableProperty]
    private string _total = "0.00";

    [ObservableProperty]
    private string _status = "Scan an item to start a bill.";

    [ObservableProperty]
    private bool _busy;

    public SalesViewModel(
        IScanItem scanner,
        IQuoteSale quoter,
        ICompleteSale sales,
        ITillSessionProvider sessions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(quoter);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scanner = scanner;
        _quoter = quoter;
        _sales = sales;
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    /// <summary>The lines on the bill, in the order they were scanned.</summary>
    public ObservableCollection<SaleLineViewModel> Lines { get; } = [];

    /// <summary>
    /// Adds the scanned symbol to the bill, then asks the Application layer to re-price it.
    /// </summary>
    [RelayCommand]
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        var barcode = Barcode.Trim();
        if (barcode.Length == 0 || Busy)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var item = await _scanner.ScanAsync(barcode, cancellationToken).ConfigureAwait(true);
                if (item is null)
                {
                    Status = "No item found for " + barcode + ".";
                    return;
                }

                _bill.Add(new SaleLineRequest(item.ProductVariantId, 1m));
                Barcode = string.Empty;

                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = item.Description + " added.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Completes the bill: one Application call, one transaction, one receipt in the outbox.
    /// </summary>
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

                var quote = await _quoter.QuoteAsync(_bill, cancellationToken).ConfigureAwait(true);

                var completed = await _sales.CompleteAsync(
                    new CompleteSaleCommand(
                        session.UserId,
                        session.ShiftId,
                        _timeProvider.GetLocalNow(),
                        [.. _bill],
                        [new TenderRequest(TenderTypes.Cash, quote.Total)]),
                    cancellationToken).ConfigureAwait(true);

                _bill.Clear();
                Lines.Clear();
                Total = "0.00";
                Status = "Saved as " + completed.BillNo + ". The receipt is queued.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var quote = await _quoter.QuoteAsync(_bill, cancellationToken).ConfigureAwait(true);

        Lines.Clear();
        foreach (var line in quote.Lines)
        {
            Lines.Add(new SaleLineViewModel(line));
        }

        Total = quote.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

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
