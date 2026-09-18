using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Ui.Services;

/// <summary>
/// What a viewmodel hosted inside <see cref="IDialogService.ShowEditDialogAsync{TViewModel}"/>
/// must expose so the shared shell (<c>EditDialogWindow</c>) can drive Save without knowing
/// anything about the specific record it is creating or editing (SRS UI-15, AC-23).
/// </summary>
public interface IEditDialogContent
{
    /// <summary>
    /// Attempts to save. Returns <see langword="true"/> when the dialog should close;
    /// <see langword="false"/> when it should stay open (for example, because the content
    /// viewmodel found a plain-language reason not to proceed and has already recorded it, SRS
    /// UI-06). An exception left to escape is caught by the dialog shell and shown as
    /// <c>EditDialogWindowViewModel.ErrorMessage</c> without closing the dialog.
    /// </summary>
    public Task<bool> SaveAsync(CancellationToken cancellationToken);
}
