using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Parks an in-progress bill and recalls it later (SRS FR-3.32, FR-3.33 - F5/F6 on the sales
/// screen, UI-02).
/// </summary>
/// <remarks>
/// No <c>[RequiresRole]</c>: holding and recalling a bill is an ordinary cashier action, the same
/// as scanning an item. Backed by <c>held_bill</c>, a plain table with no append-only trigger
/// (docs/01_DATA_MODEL.md §5) - unlike a completed sale, a held bill is not evidence of a
/// transaction, so it is deleted once recalled rather than being kept and marked spent.
/// </remarks>
public interface IHeldBillService
{
    /// <summary>Parks <paramref name="payload"/> under <paramref name="label"/> and returns the held bill's id.</summary>
    /// <exception cref="ArgumentException"><paramref name="label"/> is blank.</exception>
    public Task<long> HoldAsync(
        string label,
        HeldBillPayload payload,
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>Every held bill, newest first - what F6 lists (SRS FR-3.32: "multiple held bills must be supported").</summary>
    public Task<IReadOnlyList<HeldBillSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads back a held bill's payload and removes it - a bill recalled is no longer parked.
    /// </summary>
    /// <exception cref="InvalidOperationException">No held bill has this id.</exception>
    public Task<HeldBillPayload> RecallAsync(long heldBillId, CancellationToken cancellationToken = default);
}

/// <summary>One row of the F6 held-bill list.</summary>
/// <param name="Id">The held bill's id, passed back to <see cref="IHeldBillService.RecallAsync"/>.</param>
/// <param name="Label">What the cashier called it (SRS FR-3.32 - "each labelled").</param>
/// <param name="CreatedAt">When it was held.</param>
/// <param name="LineCount">How many lines it has, so the list is useful without recalling first.</param>
public sealed record HeldBillSummary(long Id, string Label, DateTimeOffset CreatedAt, int LineCount);
