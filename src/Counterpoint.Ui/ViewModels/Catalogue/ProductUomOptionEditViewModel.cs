using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when adding or editing one of a product's selling units
/// (SRS FR-2.4, FR-2.5, UI-15, AC-23), following <see cref="CategoryEditViewModel"/>'s pattern.
/// </summary>
/// <remarks>
/// Independent of <see cref="ProductTabViewModel"/> for the same reason
/// <see cref="ProductEditViewModel"/> is - see its own remarks.
/// <see cref="ProductTabViewModel"/> keeps its own UomFactorText/UomSellingPriceText/
/// SelectedUomToAdd properties and <c>AddUomOptionCommand</c>/<c>UpdateUomOptionCommand</c>
/// exactly as they were before this task (the protected P1-T05 test,
/// <c>ProductTabViewModelUomTests</c>, drives them directly), unused by the retrofitted view.
/// </remarks>
public sealed partial class ProductUomOptionEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly IProductMaintenance _products;
    private readonly long _productId;
    private readonly long? _editingId;

    [ObservableProperty]
    private PickerOption? _selectedUom;

    [ObservableProperty]
    private string _factorText;

    [ObservableProperty]
    private string _sellingPriceText;

    public ProductUomOptionEditViewModel(
        IProductMaintenance products,
        long productId,
        long? editingId,
        IReadOnlyList<PickerOption> addableUomOptions,
        PickerOption? initialUom,
        string initialFactorText,
        string initialSellingPriceText)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(addableUomOptions);
        ArgumentNullException.ThrowIfNull(initialFactorText);
        ArgumentNullException.ThrowIfNull(initialSellingPriceText);

        _products = products;
        _productId = productId;
        _editingId = editingId;
        AddableUomOptions = new ObservableCollection<PickerOption>(addableUomOptions);
        _selectedUom = initialUom;
        _factorText = initialFactorText;
        _sellingPriceText = initialSellingPriceText;

        // Editing an existing row keeps its own unit fixed - only the factor and selling price
        // change (IProductMaintenance.UpdateUomOptionAsync takes no unit id to change to).
        UomEditable = editingId is null;
    }

    /// <summary>Units this product does not already sell in, plus the one being edited (if any).</summary>
    public ObservableCollection<PickerOption> AddableUomOptions { get; }

    /// <summary>False when editing an existing row - the unit itself cannot change, only the factor and price.</summary>
    public bool UomEditable { get; }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (SelectedUom?.Id is not { } uomId)
        {
            throw new InvalidOperationException("Pick a unit first.");
        }

        var command = new SaveProductUomCommand(
            uomId,
            ParseUomConversion(FactorText),
            ParseOptionalDecimal(SellingPriceText) is { } price ? Money.FromDecimal(price) : null);

        if (_editingId is { } id)
        {
            await _products.UpdateUomOptionAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _products.AddUomOptionAsync(_productId, command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// As <see cref="ProductTabViewModel"/>'s own copy of this parsing rule (SRS UI-06): a blank
    /// or non-positive conversion factor is an owner mistake, shown as a sentence, never a crash.
    /// </summary>
    private static UomConversion ParseUomConversion(string text)
    {
        var factor = ParseDecimal(text);
        if (factor <= 0m)
        {
            throw new InvalidOperationException("Enter a conversion factor greater than zero.");
        }

        return UomConversion.FromDecimal(factor);
    }

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    private static decimal? ParseOptionalDecimal(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
