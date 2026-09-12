using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Purchasing;

/// <summary>One line of a purchase order being raised (SRS FR-4.5).</summary>
/// <param name="ProductVariantId">The variant being ordered.</param>
/// <param name="UomId">The unit it is being ordered in - any unit the product sells in, not necessarily its base unit (FR-2.4, FR-2.5).</param>
/// <param name="Quantity">How many, in <paramref name="UomId"/>.</param>
/// <param name="UnitCost">The expected cost per one of <paramref name="UomId"/>.</param>
public sealed record CreatePurchaseOrderLineCommand(long ProductVariantId, long UomId, decimal Quantity, Money UnitCost);

/// <summary>Raises a new purchase order, as <see cref="IPurchaseOrderService.CreateAsync"/> is asked (SRS FR-4.5).</summary>
/// <param name="SupplierId">The supplier the order is raised to.</param>
/// <param name="ExpectedAt">When the supplier is expected to deliver, or null.</param>
/// <param name="Note">Free text, or null.</param>
/// <param name="Lines">Every line to put on the order. Never empty.</param>
public sealed record CreatePurchaseOrderCommand(
    long SupplierId,
    DateTimeOffset? ExpectedAt,
    string? Note,
    IReadOnlyList<CreatePurchaseOrderLineCommand> Lines);
