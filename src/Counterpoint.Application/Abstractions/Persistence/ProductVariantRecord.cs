using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>product_variant</c> (docs/01_DATA_MODEL.md §3).</summary>
public sealed record ProductVariantRecord(
    long Id,
    long ProductId,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    Money Price,
    bool Active);
