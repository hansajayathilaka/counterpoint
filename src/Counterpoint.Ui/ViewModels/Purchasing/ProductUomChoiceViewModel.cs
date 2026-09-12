using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Purchasing;

/// <summary>One unit a picked product may be ordered in (SRS FR-2.4, FR-2.5, FR-4.5).</summary>
public sealed class ProductUomChoiceViewModel
{
    public ProductUomChoiceViewModel(ProductUomRecord uom)
    {
        ArgumentNullException.ThrowIfNull(uom);

        UomId = uom.UomId;
        Symbol = uom.UomSymbol;
        DisplayText = uom.IsBase ? uom.UomSymbol + " (base unit)" : uom.UomSymbol;
    }

    public long UomId { get; }

    public string Symbol { get; }

    public string DisplayText { get; }

    public override string ToString() => DisplayText;
}
