using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// <see cref="IXReportPrintService"/>: generates the report, renders it, and prints it - nothing
/// else (task P3-T02 "Do this" #2).
/// </summary>
public sealed class XReportPrintService : IXReportPrintService
{
    private const string DocumentType = "X_REPORT";

    private readonly IXReportService _reports;
    private readonly IXReportReceiptRenderer _renderer;
    private readonly IReceiptPrinter _printer;

    public XReportPrintService(IXReportService reports, IXReportReceiptRenderer renderer, IReceiptPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(printer);

        _reports = reports;
        _renderer = renderer;
        _printer = printer;
    }

    /// <inheritdoc />
    public async Task<PrintOutcome> PrintAsync(long shiftId, CancellationToken cancellationToken = default)
    {
        // Reads only - IXReportService.GenerateAsync writes nothing, and neither does anything
        // below this line (CLAUDE.md invariant 7: no printer call inside a transaction, and here
        // there is no transaction at all).
        var report = await _reports.GenerateAsync(shiftId, cancellationToken).ConfigureAwait(false);
        var payload = _renderer.Render(report);

        return await _printer
            .PrintAsync(payload, JobName(report), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string JobName(XReportSummary report) => string.Create(
        CultureInfo.InvariantCulture,
        $"{DocumentType}-{report.ShiftNo}");
}
