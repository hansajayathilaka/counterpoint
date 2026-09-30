using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// What every Stock, Tax and Cash report screen shares (task P3-T06): a busy flag, a status line, and the one
/// "run this read, show whatever comes back" wrapper that turns every failure into a plain sentence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Plain-language failures, detail in the log (SRS UI-06, engineering guide section 7).</b> A refusal, a
/// failed database read or an unexpected state is never shown as the exception's own text: the owner gets a
/// sentence and a next step, the log gets the exception. A <see cref="DbException"/> (every SQLite failure
/// derives from it) or an <see cref="IOException"/> while reading is logged at warning.
/// </para>
/// <para>
/// Nothing here checks a role. Each report's Application-layer query refuses a cashier session
/// (<c>[RequiresRole(Role.Owner)]</c> on the owner-only ones); the refusal arrives as a
/// <see cref="NotAuthorisedException"/> and is shown as a sentence, never worked around (SRS NFR-S2, FR-9.4, AC-17).
/// </para>
/// </remarks>
public abstract partial class ReportScreenViewModelBase : ViewModelBase
{
    private readonly ILogger _logger;

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    protected ReportScreenViewModelBase(ILogger? logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Whether there is a sentence worth showing under the report.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>The sentence a refused run shows (internal so a test can pin the wording).</summary>
    internal static string RefusedText(string report) =>
        $"The {report} report is for the owner. Sign in as the owner to see it.";

    internal static string ReadFailedText(string report) =>
        $"The {report} report could not be read from the shop's database. Wait a moment and press Run again. "
        + "If it keeps failing, contact support - the details are in the log.";

    internal static string NotProducedText(string report) =>
        $"The {report} report could not be produced. Nothing has been changed. Press Run again; "
        + "if it keeps failing, contact support - the details are in the log.";

    /// <summary>Runs one report read with the screen locked, turning every failure into a sentence.</summary>
    protected async Task RunGuardedAsync(string report, Func<Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(operation);

        var alreadyBusy = Busy;
        Busy = true;
        Status = string.Empty;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (NotAuthorisedException)
        {
            Status = RefusedText(report);
        }
        catch (Exception exception) when (exception is DbException or IOException)
        {
            ReadFailed(_logger, exception, report);
            Status = ReadFailedText(report);
        }
        catch (InvalidOperationException exception)
        {
            NotProduced(_logger, exception, report);
            Status = NotProducedText(report);
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

    [LoggerMessage(EventId = 3601, Level = LogLevel.Warning, Message = "The {Report} report could not read the database.")]
    private static partial void ReadFailed(ILogger logger, Exception exception, string report);

    [LoggerMessage(EventId = 3602, Level = LogLevel.Error, Message = "The {Report} report could not be produced.")]
    private static partial void NotProduced(ILogger logger, Exception exception, string report);
}
