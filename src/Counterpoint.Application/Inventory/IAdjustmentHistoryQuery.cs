using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The adjustment/damage history line (task P2-T08 "Do this" #4: "Adjustment history report line
/// (feeds Phase 3 exceptions)") - every <c>ADJUSTMENT</c> and <c>DAMAGE</c> movement
/// <see cref="IPostAdjustment"/> has ever posted, filterable by type so a damage write-off can be
/// pulled apart from a plain count correction (task P2-T08's last "Done when": "separately
/// reportable").
/// </summary>
/// <remarks>
/// <para>
/// Owner-only: every row carries <see cref="AdjustmentHistoryLine.UnitCost"/>, which is cost
/// (CLAUDE.md invariant 8). Unlike <see cref="IStockEnquiry"/>, which strips cost at the
/// projection so a cashier can still read the rest of the same result, there is no cashier-facing
/// half of a shrinkage report to strip it down to - a cashier has no reason to see the shop's own
/// adjustment/damage history at all, so the whole interface is gated rather than one field of it.
/// </para>
/// <para>
/// This is the read side a Phase 3 exceptions screen consumes (P3-T08); it is not that screen -
/// task P2-T08's own boundary is "Adjustment history report line", not a report UI.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IAdjustmentHistoryQuery
{
    /// <summary>Every adjustment/damage movement matching <paramref name="filter"/>, newest first.</summary>
    public Task<IReadOnlyList<AdjustmentHistoryLine>> ListAsync(
        AdjustmentHistoryFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="IAdjustmentHistoryQuery.ListAsync"/> narrows its result to.</summary>
/// <param name="Type">
/// Restricts to one movement type - <see cref="AdjustmentType.Damage"/> alone is the "separately
/// reportable" half of task P2-T08's last "Done when". Null returns both.
/// </param>
/// <param name="From">The earliest <c>occurred_at</c> to include, inclusive. Null is unbounded.</param>
/// <param name="To">The latest <c>occurred_at</c> to include, inclusive. Null is unbounded.</param>
public sealed record AdjustmentHistoryFilter(
    AdjustmentType? Type = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
