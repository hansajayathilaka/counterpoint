using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the brand list (SRS FR-2.21).</summary>
public sealed class BrandRowViewModel
{
    public BrandRowViewModel(BrandRecord brand)
    {
        ArgumentNullException.ThrowIfNull(brand);

        Id = brand.Id;
        Name = brand.Name;
        Active = brand.Active;
        StateText = brand.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Name { get; }

    public bool Active { get; }

    public string StateText { get; }
}
