using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of the dashboard's recent-sales list (SRS FR-9.7, task P3-T22).</summary>
/// <param name="BillNo">The bill's own document number (CLAUDE.md invariant 4).</param>
/// <param name="CompletedAt">When the bill was completed (<c>sale.sold_at</c>).</param>
/// <param name="CustomerName">The named customer, or <c>Walk-in</c> for an anonymous sale.</param>
/// <param name="Total">What the bill came to. No cost or margin field - CLAUDE.md invariant 8.</param>
public sealed record RecentSale(
    string BillNo,
    DateTimeOffset CompletedAt,
    string CustomerName,
    Money Total);
