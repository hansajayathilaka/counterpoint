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
        Active = uom.Active;
        StateText = uom.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Name { get; }

    public string Symbol { get; }

    public int DecimalPlaces { get; }

    public bool Active { get; }

    public string StateText { get; }
}
