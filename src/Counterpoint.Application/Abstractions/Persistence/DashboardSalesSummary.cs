using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Today's completed sales, for the dashboard (SRS FR-9.7).</summary>
/// <param name="BillCount">How many bills completed today.</param>
/// <param name="Total">The sum of their totals.</param>
public sealed record DashboardSalesSummary(int BillCount, Money Total);
