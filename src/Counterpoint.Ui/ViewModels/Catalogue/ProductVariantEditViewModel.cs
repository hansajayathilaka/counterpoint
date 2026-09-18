using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one product variant (SRS FR-2.6,
/// UI-15, AC-23), following <see cref="CategoryEditViewModel"/>'s pattern.
/// </summary>
/// <remarks>
/// Independent of <see cref="ProductTabViewModel"/> for the same reason
/// <see cref="ProductEditViewModel"/> is - see its own remarks.
/// <see cref="ProductTabViewModel"/> keeps its own VariantSku/VariantAttributesText/
/// VariantPriceText properties and <c>SaveVariantCommand</c> exactly as they were before this
/// task, unused by the retrofitted view.
/// </remarks>
public sealed partial class ProductVariantEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly IProductMaintenance _products;
    private readonly long _productId;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _sku;

    [ObservableProperty]
    private string _attributesText;

    [ObservableProperty]
    private string _priceText;

    public ProductVariantEditViewModel(
        IProductMaintenance products,
        long productId,
        long? editingId,
        string initialSku,
        string initialAttributesText,
        string initialPriceText)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(initialSku);
        ArgumentNullException.ThrowIfNull(initialAttributesText);
        ArgumentNullException.ThrowIfNull(initialPriceText);

        _products = products;
        _productId = productId;
        _editingId = editingId;
        _sku = initialSku;
        _attributesText = initialAttributesText;
        _priceText = initialPriceText;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveProductVariantCommand(
            Sku,
            ParseAttributes(AttributesText),
            Money.FromDecimal(ParseDecimal(PriceText)));

        if (_editingId is { } id)
        {
            await _products.UpdateVariantAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _products.CreateVariantAsync(_productId, command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var attributes = new Dictionary<string, string>();

        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Length > 0)
            {
                attributes[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return attributes;
    }

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
}
