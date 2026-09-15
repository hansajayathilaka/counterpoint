using System;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Cash;

/// <summary>
/// A drawer open with no sale behind it - to make change, to inspect the float, or for any other
/// reason the cashier is never trusted to open the drawer for on their own (SRS FR-7.7, task
/// P3-T01 "Do this" #5: "owner authorised, audited").
/// </summary>
/// <param name="ShiftId">The open shift this happened during.</param>
/// <param name="UserId">The cashier asking - must be the signed-in user (SRS FR-1.1).</param>
/// <param name="OccurredAt">When.</param>
/// <param name="OwnerOverride">
/// A token from <see cref="IOwnerOverrideService.RequestAsync"/> for
/// <see cref="CashMovementAuditActions.NoSaleDrawerOpen"/>. Mandatory, unlike
/// <see cref="RecordCashOutCommand.OwnerOverride"/>: there is no threshold below which a no-sale
/// open needs nobody's say-so.
/// </param>
/// <param name="PrintSlip">Whether to queue a printed ticket for this open.</param>
public sealed record NoSaleDrawerCommand(
    long ShiftId,
    long UserId,
    DateTimeOffset OccurredAt,
    OverrideToken OwnerOverride,
    bool PrintSlip = false);
