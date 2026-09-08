using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Security;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// What every reference-data tab on the catalogue screen shares: a status line, a busy flag, and
/// the same "run this, show whatever comes back" wrapper <see cref="UserAdminViewModel"/> uses.
/// </summary>
/// <remarks>
/// Deliberately plain, as the sales screen and the user-admin screen are: this is the shape of
/// the operations, not the shape of the finished till. Nothing here checks a role - every command
/// this calls is owner-only in the Application layer, and a refusal is just another
/// <see cref="NotAuthorisedException"/> shown as a sentence (SRS NFR-S2, AC-17).
/// </remarks>
public abstract partial class ReferenceDataTabViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private bool _busy;

    /// <summary>
    /// Runs an Application call with the tab locked, and turns anything that comes back into a
    /// sentence the owner can act on (SRS UI-06).
    /// </summary>
    protected async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        var alreadyBusy = Busy;
        Busy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (NotAuthorisedException exception)
        {
            // The Application layer refused. Shown, not worked around: this screen has no route
            // to the service that does not pass the check.
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
