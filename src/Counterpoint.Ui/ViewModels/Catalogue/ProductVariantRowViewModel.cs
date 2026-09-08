using System;
using System.Globalization;
using System.Linq;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of a product's variant grid (SRS FR-2.6).</summary>
public sealed class ProductVariantRowViewModel
{
    public ProductVariantRowViewModel(ProductVariantRecord variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        Id = variant.Id;
        Sku = variant.Sku;
        AttributesText = string.Join("; ", variant.Attributes.Select(pair => pair.Key + "=" + pair.Value));
        PriceText = variant.Price.Amount.ToString(CultureInfo.InvariantCulture);
        Active = variant.Active;
        StateText = variant.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Sku { get; }

    public string AttributesText { get; }

    public string PriceText { get; }

    public bool Active { get; }

    public string StateText { get; }
}
