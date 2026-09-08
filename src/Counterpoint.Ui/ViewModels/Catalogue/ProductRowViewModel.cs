using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the product list (SRS FR-2.1-FR-2.8).</summary>
public sealed class ProductRowViewModel
{
    public ProductRowViewModel(ProductSummaryRecord product)
    {
        ArgumentNullException.ThrowIfNull(product);

        Id = product.Id;
        Code = product.Code;
        Name = product.Name;
        BaseUomSymbol = product.BaseUomSymbol;
        VariantCount = product.VariantCount;
        Active = product.Active;
        StateText = product.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; }

    public string BaseUomSymbol { get; }

    public int VariantCount { get; }

    public bool Active { get; }

    public string StateText { get; }
}
