using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one product master record (SRS
/// FR-2.1-FR-2.8, UI-15, AC-23), following <see cref="CategoryEditViewModel"/>'s pattern.
/// </summary>
/// <remarks>
/// A genuinely independent viewmodel, not <see cref="ProductTabViewModel"/> itself: the
/// <c>Counterpoint.Ui.ViewLocator</c> naming convention that resolves a dialog's content to a view
/// (<see cref="IDialogService"/>'s own remarks) maps a viewmodel's type name directly to a view's,
/// so reusing <see cref="ProductTabViewModel"/> as dialog content would render the whole tab (list
/// and all) inside the dialog, not a small edit form. <see cref="ProductTabViewModel"/> keeps its
/// own Code/Name/... properties and <c>SaveCommand</c> exactly as they were before this task -
/// unused by the retrofitted view, but required unmodified by the protected P1-T05 test
/// (<c>ProductTabViewModelUomTests</c>), which constructs <see cref="ProductTabViewModel"/>
/// directly and drives them without ever touching a view.
/// </remarks>
public sealed partial class ProductEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly IProductMaintenance _products;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _code;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _nameAlt;

    [ObservableProperty]
    private PickerOption? _selectedCategory;

    [ObservableProperty]
    private PickerOption? _selectedBrand;

    [ObservableProperty]
    private PickerOption? _selectedBaseUom;

    [ObservableProperty]
    private string _selectedTypeLabel;

    [ObservableProperty]
    private PickerOption? _selectedTaxClass;

    [ObservableProperty]
    private string _location;

    [ObservableProperty]
    private bool _nonReturnable;

    [ObservableProperty]
    private string _warrantyDaysText;

    [ObservableProperty]
    private string _notes;

    public ProductEditViewModel(
        IProductMaintenance products,
        IReadOnlyList<PickerOption> categoryOptions,
        IReadOnlyList<PickerOption> brandOptions,
        IReadOnlyList<PickerOption> baseUomOptions,
        IReadOnlyList<string> typeLabels,
        IReadOnlyList<PickerOption> taxClassOptions,
        long? editingId,
        string initialCode,
        string initialName,
        string initialNameAlt,
        PickerOption? initialCategory,
        PickerOption? initialBrand,
        PickerOption? initialBaseUom,
        string initialTypeLabel,
        PickerOption? initialTaxClass,
        string initialLocation,
        bool initialNonReturnable,
        string initialWarrantyDaysText,
        string initialNotes)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(categoryOptions);
        ArgumentNullException.ThrowIfNull(brandOptions);
        ArgumentNullException.ThrowIfNull(baseUomOptions);
        ArgumentNullException.ThrowIfNull(typeLabels);
        ArgumentNullException.ThrowIfNull(taxClassOptions);
        ArgumentNullException.ThrowIfNull(initialCode);
        ArgumentNullException.ThrowIfNull(initialName);
        ArgumentNullException.ThrowIfNull(initialNameAlt);
        ArgumentNullException.ThrowIfNull(initialTypeLabel);
        ArgumentNullException.ThrowIfNull(initialLocation);
        ArgumentNullException.ThrowIfNull(initialWarrantyDaysText);
        ArgumentNullException.ThrowIfNull(initialNotes);

        _products = products;
        _editingId = editingId;

        CategoryOptions = new ObservableCollection<PickerOption>(categoryOptions);
        BrandOptions = new ObservableCollection<PickerOption>(brandOptions);
        BaseUomOptions = new ObservableCollection<PickerOption>(baseUomOptions);
        TypeLabels = typeLabels;
        TaxClassOptions = new ObservableCollection<PickerOption>(taxClassOptions);

        _code = initialCode;
        _name = initialName;
        _nameAlt = initialNameAlt;
        _selectedCategory = initialCategory;
        _selectedBrand = initialBrand;
        _selectedBaseUom = initialBaseUom;
        _selectedTypeLabel = initialTypeLabel;
        _selectedTaxClass = initialTaxClass;
        _location = initialLocation;
        _nonReturnable = initialNonReturnable;
        _warrantyDaysText = initialWarrantyDaysText;
        _notes = initialNotes;

        // Stock is already expressed in the base unit once a product exists, so the picker is
        // shown but disabled on Edit, rather than hidden - the same "always visible, sometimes
        // refused" approach IProductMaintenance.UpdateAsync itself takes.
        BaseUomEditable = editingId is null;
    }

    public ObservableCollection<PickerOption> CategoryOptions { get; }

    public ObservableCollection<PickerOption> BrandOptions { get; }

    public ObservableCollection<PickerOption> BaseUomOptions { get; }

    public IReadOnlyList<string> TypeLabels { get; }

    public ObservableCollection<PickerOption> TaxClassOptions { get; }

    /// <summary>False once editing an existing product (SRS FR-2.4, FR-2.5).</summary>
    public bool BaseUomEditable { get; }

    /// <summary>The id <see cref="SaveAsync"/> created or updated, once it has run.</summary>
    public long? SavedProductId { get; private set; }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveProductCommand(
            Code,
            Name,
            string.IsNullOrWhiteSpace(NameAlt) ? null : NameAlt,
            SelectedCategory?.Id,
            SelectedBrand?.Id,
            SelectedBaseUom?.Id ?? 0,
            ProductTabViewModel.TypeChoices.Value(SelectedTypeLabel),
            SelectedTaxClass?.Id ?? 0,
            string.IsNullOrWhiteSpace(Location) ? null : Location,
            NonReturnable,
            ParseInt(WarrantyDaysText),
            string.IsNullOrWhiteSpace(Notes) ? null : Notes,
            MaxDiscountRate: null);

        if (_editingId is { } id)
        {
            await _products.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
            SavedProductId = id;
        }
        else
        {
            SavedProductId = await _products.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }

    private static int? ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
