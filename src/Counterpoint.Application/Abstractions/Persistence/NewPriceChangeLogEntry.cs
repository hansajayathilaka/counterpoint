using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>What <see cref="IPriceChangeLogStore.RecordAsync"/> needs to append one row (SRS FR-2.17).</summary>
public sealed record NewPriceChangeLogEntry(
    long ProductVariantId,
    Money OldPrice,
    Money NewPrice,
    DateTimeOffset ChangedAt,
    long UserId,
    string? Reason);
