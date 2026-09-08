using Counterpoint.Domain.Catalogue;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of the product list - <c>product</c> without the detail a screen only needs once something is selected.</summary>
public sealed record ProductSummaryRecord(
    long Id,
    string Code,
    string Name,
    ProductType Type,
    string BaseUomSymbol,
    int VariantCount,
    bool Active);
