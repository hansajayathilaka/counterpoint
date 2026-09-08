using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>price_change_log</c> (docs/01_DATA_MODEL.md §3, SRS FR-2.17).</summary>
public sealed record PriceChangeLogEntry(
    long Id,
    long ProductVariantId,
    Money OldPrice,
    Money NewPrice,
    DateTimeOffset ChangedAt,
    long UserId,
    string? Reason);
