using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// A <see cref="ReportScreenViewModelBase"/> with the shared date-range picker (SRS FR-9.1), for the Tax and
/// Cash reports and the stock reports that run over a period (task P3-T06).
/// </summary>
public abstract class RangedReportScreenViewModelBase : ReportScreenViewModelBase
{
    protected RangedReportScreenViewModelBase(
        TimeProvider timeProvider,
        ILogger? logger,
        ReportDatePreset initial = ReportDatePreset.ThisMonth)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        Range = new ReportRangeViewModel(timeProvider, initial);
    }

    /// <summary>The date-range picker.</summary>
    public ReportRangeViewModel Range { get; }

    /// <summary>Resolves the picker and, when it is valid, runs <paramref name="operation"/> over the range.</summary>
    protected Task RunRangedAsync(string report, Func<ReportDateRange, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return Range.TryResolve(out var range)
            ? RunGuardedAsync(report, () => operation(range), cancellationToken)
            : Task.CompletedTask;
    }
}
