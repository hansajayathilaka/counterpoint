using System;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>The printed ticket for a no-sale drawer open (SRS FR-7.7, task P3-T01 "Do this" #5).</summary>
/// <param name="ShiftNo">The shift the drawer was opened during.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="CashierName">Who asked for it.</param>
/// <param name="AuthorisedByName">The owner who authorised it.</param>
public sealed record NoSaleSlip(
    string ShiftNo,
    DateTimeOffset OccurredAt,
    string CashierName,
    string AuthorisedByName);
