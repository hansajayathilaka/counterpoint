using System;
using System.Collections.Generic;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A new <c>purchase_order</c> row and its lines, written together in one insert
/// (docs/01_DATA_MODEL.md §4, SRS FR-4.5).
/// </summary>
/// <param name="PoNo">Allocated from <c>number_sequence</c> before this reaches the store (CLAUDE.md invariant 4).</param>
/// <param name="SupplierId">The supplier the order is raised to.</param>
/// <param name="OrderedAt">When the order was raised.</param>
/// <param name="ExpectedAt">When the supplier is expected to deliver, or null.</param>
/// <param name="UserId">The owner who raised it.</param>
/// <param name="Note">Free text, or null.</param>
/// <param name="Lines">Every line on the order. Never empty.</param>
public sealed record NewPurchaseOrder(
    string PoNo,
    long SupplierId,
    DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt,
    long UserId,
    string? Note,
    IReadOnlyList<NewPurchaseOrderLine> Lines);
