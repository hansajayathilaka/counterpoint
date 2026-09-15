using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Cash;

/// <summary>
/// Opens the cash drawer with no sale behind it - always owner-authorised, always audited (SRS
/// FR-7.7, task P3-T01 "Do this" #5).
/// </summary>
/// <remarks>
/// Not owner-only in its own right - the cashier stays signed in throughout, exactly as
/// <see cref="Counterpoint.Application.Returns.ICreateUnlinkedReturn"/> does (SRS FR-1.7). What
/// makes this path high-friction is that <see cref="NoSaleDrawerCommand.OwnerOverride"/> is
/// mandatory on every call, not that the method needs anyone's role.
/// </remarks>
public interface INoSaleDrawerService
{
    /// <summary>Opens the drawer, writes the audit row, and optionally queues a printed ticket.</summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// The caller is not the signed-in user, or is not trading in the shift given.
    /// </exception>
    public Task<NoSaleDrawerResult> OpenAsync(
        NoSaleDrawerCommand command, CancellationToken cancellationToken = default);
}
