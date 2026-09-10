using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Shifts;

/// <summary>Opens a shift (SRS FR-8.1) - the minimum P1-T14 builds to make <c>sale.shift_id</c> meaningful.</summary>
/// <remarks>
/// No <c>RequiresRoleAttribute</c>: opening (and closing) a shift is an ordinary cashier
/// capability (<c>Counterpoint.Domain.Security.Role</c>, SRS ROLE-1), not an owner-only one, so
/// this is registered plain, the same as <c>ICompleteSale</c>.
/// </remarks>
public interface IOpenShift
{
    /// <summary>Opens a new shift. Refused while one is already open (C-01).</summary>
    public Task<OpenedShift> OpenAsync(OpenShiftCommand command, CancellationToken cancellationToken = default);
}
