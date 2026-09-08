using System;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the tax-class list (Q-02, FR-10.3).</summary>
public sealed class TaxClassRowViewModel
{
    public TaxClassRowViewModel(TaxClassRecord taxClass)
    {
        ArgumentNullException.ThrowIfNull(taxClass);

        Id = taxClass.Id;
        Name = taxClass.Name;
        Rate = taxClass.Rate;
        RatePercentText = SettingsText.FromTaxRate(taxClass.Rate);
        Active = taxClass.Active;
        StateText = taxClass.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Name { get; }

    public TaxRate Rate { get; }

    public string RatePercentText { get; }

    public bool Active { get; }

    public string StateText { get; }
}
