using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Sales;

/// <summary>
/// The one way a completed bill is cancelled (SRS FR-3.34, AC-17).
/// </summary>
/// <remarks>
/// Owner-only, the same mechanism every other owner-restricted service in this codebase uses
/// (<c>Counterpoint.Application.Security.RoleAuthorisation</c>, P1-T02) - not a UI confirmation,
/// which SRS NFR-S2 and CLAUDE.md invariant 8 both rule out as "authorisation".
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ICancelSale
{
    /// <summary>
    /// Cancels the bill as one transaction, or changes nothing at all - the same all-or-nothing
    /// discipline <see cref="ICompleteSale.CompleteAsync"/> keeps (SRS FR-3.30).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// No reason was given, the bill does not exist, it is already cancelled, or it was sold on
    /// an earlier business day (SRS FR-3.34).
    /// </exception>
    public Task<CancelledSale> CancelAsync(
        CancelSaleCommand command,
        CancellationToken cancellationToken = default);
}
