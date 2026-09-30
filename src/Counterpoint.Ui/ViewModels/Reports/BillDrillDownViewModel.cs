using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>Where the drill-down panel currently is.</summary>
public enum DrillLevel
{
    /// <summary>Not drilled in: the report itself shows.</summary>
    None,

    /// <summary>A list of the bills behind a summary row.</summary>
    BillList,

    /// <summary>One bill in full.</summary>
    Bill,
}

/// <summary>
/// The drill-down half of a sales report: summary row -&gt; bill list -&gt; one bill (task P3-T05 "Do this"
/// #5). One instance per report screen; it holds only what the Application layer's
/// <see cref="ISalesBillQuery"/> hands back, so the bill it opens is the bill the row named.
/// </summary>
public sealed partial class BillDrillDownViewModel : ViewModelBase
{
    private readonly ISalesBillQuery _bills;
    private bool _cameFromList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsBillList))]
    [NotifyPropertyChangedFor(nameof(IsBill))]
    private DrillLevel _level = DrillLevel.None;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    [ObservableProperty]
    private BillDetailViewModel? _bill;

    [ObservableProperty]
    private bool _busy;

    public BillDrillDownViewModel(ISalesBillQuery bills)
    {
        ArgumentNullException.ThrowIfNull(bills);
        _bills = bills;
    }

    /// <summary>The bills behind the summary row last drilled into.</summary>
    public ObservableCollection<BillRowViewModel> Bills { get; } = [];

    /// <summary>True while either drill level shows instead of the report.</summary>
    public bool IsActive => Level != DrillLevel.None;

    /// <summary>True while the bill list shows.</summary>
    public bool IsBillList => Level == DrillLevel.BillList;

    /// <summary>True while one bill shows.</summary>
    public bool IsBill => Level == DrillLevel.Bill;

    /// <summary>True when there is a note (for example "list cut short") worth showing.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Lists the bills matching <paramref name="filter"/> under <paramref name="title"/>.</summary>
    public async Task OpenBillListAsync(BillListFilter filter, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await RunAsync(
            async () =>
            {
                var list = await _bills.GetBillsAsync(filter, cancellationToken).ConfigureAwait(true);

                Bills.Clear();
                foreach (var row in list.Rows)
                {
                    Bills.Add(new BillRowViewModel(row, OpenBillCommand));
                }

                Title = title;
                Note = list.IsTruncated
                    ? "Showing the first " + list.Rows.Count.ToString(CultureInfo.InvariantCulture)
                        + " bills - narrow the date range to see the rest."
                    : Bills.Count == 0
                        ? "No bills."
                        : Bills.Count.ToString(CultureInfo.InvariantCulture) + (Bills.Count == 1 ? " bill." : " bills.");
                Bill = null;
                Level = DrillLevel.BillList;
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens one bill in full.</summary>
    [RelayCommand]
    public async Task OpenBillAsync(BillRowViewModel row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        await OpenBillByIdAsync(row.SaleId, fromList: true, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the bill with <paramref name="saleId"/>, straight from a report row that names one.</summary>
    public async Task OpenBillByIdAsync(long saleId, bool fromList, CancellationToken cancellationToken = default)
    {
        await RunAsync(
            async () =>
            {
                var detail = await _bills.GetBillAsync(saleId, cancellationToken).ConfigureAwait(true);

                if (detail is null)
                {
                    Note = "That bill could not be found.";
                    return;
                }

                Bill = new BillDetailViewModel(detail);
                Title = "Bill " + detail.BillNo;
                _cameFromList = fromList;
                Level = DrillLevel.Bill;
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>One step back: bill to its list (when it came from one), list to the report.</summary>
    [RelayCommand]
    public void Back()
    {
        if (Level == DrillLevel.Bill && _cameFromList)
        {
            Bill = null;
            Level = DrillLevel.BillList;
            return;
        }

        Close();
    }

    /// <summary>Returns to the report.</summary>
    [RelayCommand]
    public void Close()
    {
        Bill = null;
        Bills.Clear();
        Note = string.Empty;
        Level = DrillLevel.None;
    }

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
            Note = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            Note = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Note = "Cancelled.";
        }
        finally
        {
            Busy = alreadyBusy;
        }
    }
}
