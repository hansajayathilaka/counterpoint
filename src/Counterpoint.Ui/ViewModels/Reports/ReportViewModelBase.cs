using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// What every report screen shares: a range picker, a busy flag, a status line and the shared
/// drill-down panel, plus the one "run this, show whatever comes back" wrapper.
/// </summary>
/// <remarks>
/// Nothing here checks a role. Each report's Application-layer query is what refuses a cashier
/// session (<c>[RequiresRole(Role.Owner)]</c> on the owner-only ones); a refusal arrives as a
/// <see cref="NotAuthorisedException"/> and is shown as a sentence, never worked around (SRS NFR-S2,
/// FR-9.4, AC-17).
/// </remarks>
public abstract partial class ReportViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    protected ReportViewModelBase(TimeProvider timeProvider, BillDrillDownViewModel drill)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(drill);

        Range = new ReportRangeViewModel(timeProvider);
        Drill = drill;
    }

    /// <summary>The date-range picker.</summary>
    public ReportRangeViewModel Range { get; }

    /// <summary>The summary -&gt; bill list -&gt; bill drill-down panel (SRS FR-9, task P3-T05 "Do this" #5).</summary>
    public BillDrillDownViewModel Drill { get; }

    /// <summary>Whether there is a sentence worth showing under the report.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>The range the last successful run used; drill-downs from a summary row stay inside it.</summary>
    protected ReportDateRange? LastRange { get; set; }

    /// <summary>
    /// Runs one report read with the screen locked. Turns a refusal or a cancellation into a sentence
    /// the owner can act on (SRS UI-06).
    /// </summary>
    protected async Task GuardedAsync(Func<ReportDateRange, Task> operation, CancellationToken cancellationToken)
    {
        if (!Range.TryResolve(out var range))
        {
            return;
        }

        var alreadyBusy = Busy;
        Busy = true;
        Status = string.Empty;
        try
        {
            await operation(range).ConfigureAwait(true);
            LastRange = range;
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
