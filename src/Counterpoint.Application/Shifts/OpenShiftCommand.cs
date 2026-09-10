using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>Opens a new shift with an opening cash float (SRS FR-8.1).</summary>
/// <param name="UserId">The cashier opening it - must be the signed-in user.</param>
/// <param name="OpeningFloat">The cash counted into the drawer before trading starts. Never negative.</param>
/// <param name="OpenedAt">When the shift opens. Its date is the shift's business date.</param>
public sealed record OpenShiftCommand(long UserId, Money OpeningFloat, DateTimeOffset OpenedAt);
