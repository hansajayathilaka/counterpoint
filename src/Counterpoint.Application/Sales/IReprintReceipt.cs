using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Reprints any past bill, marked <c>DUPLICATE</c> and logged (SRS FR-3.36, FR-7.5, FR-7.6).
/// </summary>
/// <remarks>
/// Any signed-in cashier may reprint (SRS §3.3 ROLE-1) - unlike <c>ICancelSale</c>, this carries
/// no <c>RequiresRoleAttribute</c> and is registered undecorated, the same shape as
/// <c>ICompleteSale</c>.
/// </remarks>
public interface IReprintReceipt
{
    /// <summary>Reprints the given bill.</summary>
    /// <exception cref="System.InvalidOperationException">No such bill exists, or nobody is signed in.</exception>
    public Task<ReprintedReceipt> ReprintAsync(long saleId, CancellationToken cancellationToken = default);
}
