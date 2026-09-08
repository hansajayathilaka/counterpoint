using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the unit-of-measure list.</summary>
public sealed class UomRowViewModel
{
    public UomRowViewModel(UomRecord uom)
    {
        ArgumentNullException.ThrowIfNull(uom);

        Id = uom.Id;
        Name = uom.Name;
        Symbol = uom.Symbol;
        DecimalPlaces = uom.DecimalPlaces;
    }

    public long Id { get; }

    public string Name { get; }

    public string Symbol { get; }

    public int DecimalPlaces { get; }
}
