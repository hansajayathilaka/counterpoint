using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Application.Cash;

/// <summary>
/// Cash in and cash out (SRS FR-8.2, task P3-T01 "Do this" #1, #3, #4) - the service a cash-drawer
/// screen calls.
/// </summary>
/// <remarks>
/// Not owner-only in its own right - a cashier tops up the float or pays out a small petty expense
/// without needing anyone's role, the same "ask the owner only over the limit" shape
/// <see cref="Counterpoint.Application.Pricing.IDiscountAuthorisationService"/> and
/// <see cref="Counterpoint.Application.Returns.IReturnPolicyAuthorisationService"/> already draw.
/// What is gated is a cash-out above <c>policy.cash_out_authorisation_threshold</c>, and that gate
/// is <see cref="Counterpoint.Application.Security.OverrideToken"/>, the same single-use,
/// re-authenticated mechanism every owner override uses.
/// </remarks>
public interface ICashMovementService
{
    /// <summary>Records money added to the drawer (SRS FR-8.2).</summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// The caller is not the signed-in user, or is not trading in the shift given.
    /// </exception>
    public Task<RecordedCashMovement> RecordCashInAsync(
        RecordCashInCommand command, CancellationToken cancellationToken = default);

    /// <summary>Records money taken out of the drawer (SRS FR-8.2).</summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// The caller is not the signed-in user, or is not trading in the shift given.
    /// </exception>
    /// <exception cref="CashOutAuthorisationRequiredException">
    /// <see cref="RecordCashOutCommand.Amount"/> is above <c>policy.cash_out_authorisation_threshold</c>
    /// and <see cref="RecordCashOutCommand.OwnerOverride"/> does not authorise it (task P3-T01
    /// "Do this" #3).
    /// </exception>
    public Task<RecordedCashMovement> RecordCashOutAsync(
        RecordCashOutCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// This shift's cash movement history, newest first (task P3-T01 "Do this" #4: "visible to the
    /// cashier for their own shift").
    /// </summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// A cashier asked for a shift other than the one they are currently trading in. An owner may
    /// ask for any shift.
    /// </exception>
    public Task<IReadOnlyList<CashMovementRecord>> GetHistoryAsync(
        long shiftId, CancellationToken cancellationToken = default);
}
