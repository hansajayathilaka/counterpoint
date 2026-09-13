using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Exchanges;

/// <summary>
/// The one way an exchange is taken (SRS FR-5 exchange, AC-04, task P2-T04).
/// </summary>
/// <remarks>
/// Not owner-only in its own right - the same reasoning as
/// <see cref="Counterpoint.Application.Returns.ICreateReturn"/>: a cashier takes an ordinary,
/// in-window, receipted exchange without needing anyone's role, and every exception to that is
/// gated by an <see cref="Counterpoint.Application.Security.OverrideToken"/>, not
/// <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/>.
/// </remarks>
public interface ICreateExchange
{
    /// <summary>
    /// Takes the exchange as one transaction - one <c>sale_return</c> and one <c>sale</c>,
    /// cross-linked, or changes nothing at all (SRS FR-3.30's exchange-side cousin).
    /// </summary>
    /// <exception cref="Counterpoint.Application.Returns.ReturnNotEligibleException">
    /// A return policy rule refused the return half and no override authorises it.
    /// </exception>
    public Task<CreatedExchange> CreateAsync(
        CreateExchangeCommand command, CancellationToken cancellationToken = default);
}
