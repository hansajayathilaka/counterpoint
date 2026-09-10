using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The print queue screen: every pending or failed <c>print_job</c> row, with a retry button
/// (P1-T11's "Print queue UI: pending and failed jobs, retry, with the status-bar indicator").
/// </summary>
/// <remarks>
/// No business logic here, the same discipline <c>SalesViewModel</c> keeps: this reads
/// <see cref="IPrintJobOutbox"/> and shows what comes back, and asks it to retry a job. Deciding
/// what "failed" or "retry" mean lives in <c>SqlitePrintJobOutbox</c> and <c>PrintWorker</c>.
/// </remarks>
public sealed partial class PrintQueueViewModel : ObservableObject
{
    private readonly IPrintJobOutbox _printJobs;

    public PrintQueueViewModel(IPrintJobOutbox printJobs)
    {
        ArgumentNullException.ThrowIfNull(printJobs);
        _printJobs = printJobs;
    }

    /// <summary>Every pending or failed job, oldest first.</summary>
    public ObservableCollection<PrintQueueEntry> Jobs { get; } = [];

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// "2 pending, 1 failed" - or "Print queue empty" - for a status-bar indicator elsewhere on
    /// the screen (the other half of this task's "with the status-bar indicator").
    /// </summary>
    public string Summary
    {
        get
        {
            var pending = Jobs.Count(job => job.Status == "PENDING");
            var failed = Jobs.Count(job => job.Status == "FAILED");

            if (pending == 0 && failed == 0)
            {
                return "Print queue empty";
            }

            return failed == 0
                ? pending.ToString(CultureInfo.InvariantCulture) + " pending"
                : pending.ToString(CultureInfo.InvariantCulture) + " pending, "
                    + failed.ToString(CultureInfo.InvariantCulture) + " failed";
        }
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var jobs = await _printJobs.ListQueueAsync(cancellationToken).ConfigureAwait(true);

        Jobs.Clear();
        foreach (var job in jobs)
        {
            Jobs.Add(job);
        }

        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    public async Task RetryAsync(PrintQueueEntry? job, CancellationToken cancellationToken)
    {
        if (job is null)
        {
            return;
        }

        await _printJobs.RetryAsync(job.Id, cancellationToken).ConfigureAwait(true);
        Status = "Job " + job.Id.ToString(CultureInfo.InvariantCulture) + " queued for retry.";

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }
}
