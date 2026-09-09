using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Sales;

/// <summary>
/// A bill's total, split across one or more tenders (SRS FR-3.24, FR-3.25, FR-3.26).
/// </summary>
/// <param name="Applied">
/// What is recorded against the bill - one row per <see cref="TenderLine"/> offered, each capped
/// at what the bill still owed when it was taken. Sums to exactly the bill total
/// (<see cref="TenderCalculator"/> never returns a plan that does not).
/// </param>
/// <param name="Change">What goes back to the customer in cash. Zero when nothing is owed back.</param>
public sealed record TenderPlan(IReadOnlyList<AppliedTender> Applied, Money Change);
