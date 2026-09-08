using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The product tab of the catalogue screen: a product editor with a variant grid, a
/// unit-of-measure grid, and a variant matrix generator (docs/01_DATA_MODEL.md §3, §8, SRS
/// FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
/// <remarks>
/// One flat viewmodel rather than several composed ones: the three grids only ever act on
/// whichever product is currently selected, and threading that selection through separate
/// viewmodel boundaries would cost more wiring than the one screen it serves.
/// </remarks>
public sealed partial class ProductTabViewModel : ReferenceDataTabViewModel
{
    private static readonly EnumChoices<ProductType> TypeChoices = new(
        (ProductType.Standard, "Standard (whole units)"),
        (ProductType.Fractional, "Decimal (fractional units)"),
        (ProductType.Service, "Service"),
        (ProductType.NonInventory, "Non-inventory"));

    private readonly IProductMaintenance _products;
    private readonly ICategoryMaintenance _categories;
    private readonly IBrandMaintenance _brands;
    private readonly IUomMaintenance _uoms;
    private readonly ITaxClassMaintenance _taxClasses;

    private long? _editingId;
    private long? _editingVariantId;
    private IReadOnlyList<VariantAttributes> _previewedCombinations = [];

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _nameAlt = string.Empty;

    [ObservableProperty]
    private PickerOption? _selectedCategory;

    [ObservableProperty]
    private PickerOption? _selectedBrand;

    [ObservableProperty]
    private PickerOption? _selectedBaseUom;

    [ObservableProperty]
    private string _selectedTypeLabel = TypeChoices.Labels[0];

    [ObservableProperty]
    private PickerOption? _selectedTaxClass;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private bool _nonReturnable;

    [ObservableProperty]
    private string _warrantyDaysText = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    /// <summary>
    /// False once a product exists: stock is already expressed in its base unit, so the picker is
    /// shown but disabled rather than hidden - the same "always visible, sometimes refused"
    /// approach the Application layer takes (<see cref="IProductMaintenance.UpdateAsync"/>).
    /// </summary>
    [ObservableProperty]
    private bool _baseUomEditable = true;

    [ObservableProperty]
    private ProductRowViewModel? _selectedItem;

    [ObservableProperty]
    private string _variantSku = string.Empty;

    [ObservableProperty]
    private string _variantAttributesText = string.Empty;

    [ObservableProperty]
    private string _variantPriceText = string.Empty;

    [ObservableProperty]
    private ProductVariantRowViewModel? _selectedVariant;

    [ObservableProperty]
    private PickerOption? _selectedUomToAdd;

    [ObservableProperty]
    private string _uomFactorText = string.Empty;

    [ObservableProperty]
    private string _uomSellingPriceText = string.Empty;

    [ObservableProperty]
    private ProductUomOptionRowViewModel? _selectedUomOption;

    [ObservableProperty]
    private string _matrixAxesText = string.Empty;

    [ObservableProperty]
    private string _matrixDefaultPriceText = string.Empty;

    [ObservableProperty]
    private string _matrixStatus = string.Empty;

    public ProductTabViewModel(
        IProductMaintenance products,
        ICategoryMaintenance categories,
        IBrandMaintenance brands,
        IUomMaintenance uoms,
        ITaxClassMaintenance taxClasses)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(brands);
        ArgumentNullException.ThrowIfNull(uoms);
        ArgumentNullException.ThrowIfNull(taxClasses);

        _products = products;
        _categories = categories;
        _brands = brands;
        _uoms = uoms;
        _taxClasses = taxClasses;
    }

    public ObservableCollection<ProductRowViewModel> Items { get; } = [];

    public ObservableCollection<PickerOption> CategoryOptions { get; } = [];

    public ObservableCollection<PickerOption> BrandOptions { get; } = [];

    public ObservableCollection<PickerOption> BaseUomOptions { get; } = [];

    public ObservableCollection<PickerOption> TaxClassOptions { get; } = [];

    public ObservableCollection<PickerOption> AddableUomOptions { get; } = [];

    public static IReadOnlyList<string> TypeLabels => TypeChoices.Labels;

    public ObservableCollection<ProductVariantRowViewModel> Variants { get; } = [];

    public ObservableCollection<ProductUomOptionRowViewModel> UomOptions { get; } = [];

    public ObservableCollection<string> MatrixPreview { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var products = await _products.ListAsync(cancellationToken).ConfigureAwait(true);
                var categories = await _categories.ListAsync(cancellationToken).ConfigureAwait(true);
                var brands = await _brands.ListAsync(cancellationToken).ConfigureAwait(true);
                var uoms = await _uoms.ListAsync(cancellationToken).ConfigureAwait(true);
                var taxClasses = await _taxClasses.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var product in products)
                {
                    Items.Add(new ProductRowViewModel(product));
                }

                CategoryOptions.Clear();
                CategoryOptions.Add(new PickerOption(null, "(none)"));
                foreach (var category in categories.Where(c => c.Active))
                {
                    CategoryOptions.Add(new PickerOption(category.Id, category.Name));
                }

                BrandOptions.Clear();
                BrandOptions.Add(new PickerOption(null, "(none)"));
                foreach (var brand in brands.Where(b => b.Active))
                {
                    BrandOptions.Add(new PickerOption(brand.Id, brand.Name));
                }

                BaseUomOptions.Clear();
                TaxClassOptions.Clear();
                foreach (var uom in uoms.Where(u => u.Active))
                {
                    BaseUomOptions.Add(new PickerOption(uom.Id, uom.Name + " (" + uom.Symbol + ")"));
                }

                foreach (var taxClass in taxClasses.Where(t => t.Active))
                {
                    TaxClassOptions.Add(new PickerOption(taxClass.Id, taxClass.Name));
                }

                Status = Items.Count == 1 ? "1 product." : Items.Count + " products.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Code = string.Empty;
        Name = string.Empty;
        NameAlt = string.Empty;
        SelectedCategory = CategoryOptions.FirstOrDefault();
        SelectedBrand = BrandOptions.FirstOrDefault();
        SelectedBaseUom = BaseUomOptions.FirstOrDefault();
        SelectedTypeLabel = TypeChoices.Labels[0];
        SelectedTaxClass = TaxClassOptions.FirstOrDefault();
        Location = string.Empty;
        NonReturnable = false;
        WarrantyDaysText = string.Empty;
        Notes = string.Empty;
        BaseUomEditable = true;

        Variants.Clear();
        UomOptions.Clear();
        NewVariant();
        NewUomOption();
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var command = new SaveProductCommand(
                    Code,
                    Name,
                    string.IsNullOrWhiteSpace(NameAlt) ? null : NameAlt,
                    SelectedCategory?.Id,
                    SelectedBrand?.Id,
                    SelectedBaseUom?.Id ?? 0,
                    TypeChoices.Value(SelectedTypeLabel),
                    SelectedTaxClass?.Id ?? 0,
                    string.IsNullOrWhiteSpace(Location) ? null : Location,
                    NonReturnable,
                    ParseInt(WarrantyDaysText),
                    string.IsNullOrWhiteSpace(Notes) ? null : Notes,
                    MaxDiscountRate: null);

                if (_editingId is { } id)
                {
                    await _products.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    SelectedItem = Items.FirstOrDefault(item => item.Id == id);
                }
                else
                {
                    var id2 = await _products.CreateAsync(command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " created.";
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    SelectedItem = Items.FirstOrDefault(item => item.Id == id2);
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a product first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _products.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _products.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(ProductRowViewModel? value)
    {
        _editingId = value?.Id;
        BaseUomEditable = value is null;

        if (value is null)
        {
            return;
        }

        _ = LoadSelectedProductAsync(value.Id);
    }

    private async Task LoadSelectedProductAsync(long productId)
    {
        await RunAsync(
            async () =>
            {
                var record = await _products.FindByIdAsync(productId).ConfigureAwait(true);
                if (record is null)
                {
                    return;
                }

                Code = record.Code;
                Name = record.Name;
                NameAlt = record.NameAlt ?? string.Empty;
                SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Id == record.CategoryId) ?? CategoryOptions.FirstOrDefault();
                SelectedBrand = BrandOptions.FirstOrDefault(o => o.Id == record.BrandId) ?? BrandOptions.FirstOrDefault();
                SelectedBaseUom = BaseUomOptions.FirstOrDefault(o => o.Id == record.BaseUomId);
                SelectedTypeLabel = TypeChoices.Label(record.Type);
                SelectedTaxClass = TaxClassOptions.FirstOrDefault(o => o.Id == record.TaxClassId);
                Location = record.Location ?? string.Empty;
                NonReturnable = record.NonReturnable;
                WarrantyDaysText = record.WarrantyDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                Notes = record.Notes ?? string.Empty;

                await RefreshVariantsAsync(productId).ConfigureAwait(true);
                await RefreshUomOptionsAsync(productId).ConfigureAwait(true);
                NewVariant();
                NewUomOption();
            },
            CancellationToken.None).ConfigureAwait(true);
    }

    private async Task RefreshVariantsAsync(long productId)
    {
        var variants = await _products.ListVariantsAsync(productId).ConfigureAwait(true);

        Variants.Clear();
        foreach (var variant in variants)
        {
            Variants.Add(new ProductVariantRowViewModel(variant));
        }
    }

    private async Task RefreshUomOptionsAsync(long productId)
    {
        var options = await _products.ListUomOptionsAsync(productId).ConfigureAwait(true);

        UomOptions.Clear();
        foreach (var option in options)
        {
            UomOptions.Add(new ProductUomOptionRowViewModel(option));
        }

        var usedUomIds = options.Select(o => o.UomId).ToHashSet();
        AddableUomOptions.Clear();
        foreach (var candidate in BaseUomOptions.Where(o => o.Id is { } id && !usedUomIds.Contains(id)))
        {
            AddableUomOptions.Add(candidate);
        }

        SelectedUomToAdd = AddableUomOptions.FirstOrDefault();
    }

    [RelayCommand]
    public void NewVariant()
    {
        _editingVariantId = null;
        SelectedVariant = null;
        VariantSku = string.Empty;
        VariantAttributesText = string.Empty;
        VariantPriceText = string.Empty;
    }

    [RelayCommand]
    public async Task SaveVariantAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId)
        {
            Status = "Save the product before adding a variant.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var command = new SaveProductVariantCommand(
                    VariantSku,
                    ParseAttributes(VariantAttributesText),
                    Money.FromDecimal(ParseDecimal(VariantPriceText)));

                if (_editingVariantId is { } id)
                {
                    await _products.UpdateVariantAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = "Variant " + command.Sku + " updated.";
                }
                else
                {
                    await _products.CreateVariantAsync(productId, command, cancellationToken).ConfigureAwait(true);
                    Status = "Variant " + command.Sku + " created.";
                }

                NewVariant();
                await RefreshVariantsAsync(productId).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ToggleVariantActiveAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId || SelectedVariant is not { } selected)
        {
            Status = "Pick a variant first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _products.DeactivateVariantAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _products.ReactivateVariantAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                Status = selected.Sku + (selected.Active ? " is turned off." : " is turned back on.");
                await RefreshVariantsAsync(productId).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedVariantChanged(ProductVariantRowViewModel? value)
    {
        _editingVariantId = value?.Id;
        VariantSku = value?.Sku ?? string.Empty;
        VariantAttributesText = value?.AttributesText ?? string.Empty;
        VariantPriceText = value?.PriceText ?? string.Empty;
    }

    [RelayCommand]
    public void NewUomOption()
    {
        SelectedUomOption = null;
        SelectedUomToAdd = AddableUomOptions.FirstOrDefault();
        UomFactorText = string.Empty;
        UomSellingPriceText = string.Empty;
    }

    [RelayCommand]
    public async Task AddUomOptionAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId)
        {
            Status = "Save the product before adding a unit.";
            return;
        }

        if (SelectedUomToAdd?.Id is not { } uomId)
        {
            Status = "Pick a unit to add.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var command = new SaveProductUomCommand(
                    uomId,
                    ParseUomConversion(UomFactorText),
                    ParseOptionalDecimal(UomSellingPriceText) is { } price ? Money.FromDecimal(price) : null);

                await _products.AddUomOptionAsync(productId, command, cancellationToken).ConfigureAwait(true);

                Status = "Unit added.";
                NewUomOption();
                await RefreshUomOptionsAsync(productId).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task UpdateUomOptionAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId || SelectedUomOption is not { } selected)
        {
            Status = "Pick a unit first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var command = new SaveProductUomCommand(
                    selected.UomId,
                    ParseUomConversion(UomFactorText),
                    ParseOptionalDecimal(UomSellingPriceText) is { } price ? Money.FromDecimal(price) : null);

                await _products.UpdateUomOptionAsync(selected.Id, command, cancellationToken).ConfigureAwait(true);

                Status = "Unit updated.";
                await RefreshUomOptionsAsync(productId).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RemoveUomOptionAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId || SelectedUomOption is not { } selected)
        {
            Status = "Pick a unit first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _products.RemoveUomOptionAsync(selected.Id, cancellationToken).ConfigureAwait(true);

                Status = "Unit removed.";
                NewUomOption();
                await RefreshUomOptionsAsync(productId).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedUomOptionChanged(ProductUomOptionRowViewModel? value)
    {
        UomFactorText = value?.FactorText ?? string.Empty;
        UomSellingPriceText = value?.SellingPriceText ?? string.Empty;
    }

    [RelayCommand]
    public async Task PreviewMatrixAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId)
        {
            Status = "Save the product before generating variants.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var axes = ParseAxes(MatrixAxesText);

                if (axes.Count == 0)
                {
                    MatrixStatus = "Enter at least one axis, one per line: name: value1, value2, ...";
                    return;
                }

                var preview = await _products.PreviewVariantMatrixAsync(productId, axes, cancellationToken)
                    .ConfigureAwait(true);

                _previewedCombinations = preview.ToCreate;

                MatrixPreview.Clear();
                foreach (var combination in preview.ToCreate)
                {
                    MatrixPreview.Add(combination.ToString());
                }

                MatrixStatus = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{preview.TotalCombinations} combinations: {preview.ToCreate.Count} to create, {preview.SkippedExistingCount} already exist.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task CommitMatrixAsync(CancellationToken cancellationToken)
    {
        if (_editingId is not { } productId)
        {
            Status = "Save the product before generating variants.";
            return;
        }

        if (_previewedCombinations.Count == 0)
        {
            MatrixStatus = "Nothing to create - preview first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var defaultPrice = Money.FromDecimal(ParseDecimal(MatrixDefaultPriceText));

                var ids = await _products.CommitVariantMatrixAsync(
                    productId, _previewedCombinations, defaultPrice, cancellationToken).ConfigureAwait(true);

                MatrixStatus = ids.Count + " variant(s) created.";
                MatrixPreview.Clear();
                _previewedCombinations = [];

                await RefreshVariantsAsync(productId).ConfigureAwait(true);
            },
            cancellationToken).ConfigureAwait(true);
    }

    private static List<VariantAxis> ParseAxes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var axes = new List<VariantAxis>();

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var name = parts[0].Trim();
            var values = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (name.Length == 0 || values.Length == 0)
            {
                continue;
            }

            axes.Add(new VariantAxis(name, values));
        }

        return axes;
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

    private static int? ParseInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    /// <summary>
    /// Reads the "conversion factor" box into a <see cref="UomConversion"/>, rejecting a blank or
    /// non-positive entry with a sentence the owner can act on rather than letting
    /// <see cref="UomConversion.FromDecimal"/>'s <see cref="ArgumentOutOfRangeException"/> escape
    /// <see cref="ReferenceDataTabViewModel.RunAsync"/> uncaught (SRS UI-06).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SettingsText.ToTaxRate"/>, which clamps a blank or negative rate to zero
    /// because zero is a perfectly sensible tax rate, there is no sensible default conversion
    /// factor: "1 box = 0 pieces" or "1 box = blank pieces" is not a smaller version of a valid
    /// unit, it is a unit with no meaning, so it is refused outright rather than silently
    /// substituted.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="text"/> is blank, unparseable, or not a positive number.
    /// </exception>
    private static UomConversion ParseUomConversion(string text)
    {
        var factor = ParseDecimal(text);
        if (factor <= 0m)
        {
            throw new InvalidOperationException("Enter a conversion factor greater than zero.");
        }

        return UomConversion.FromDecimal(factor);
    }

    private static decimal? ParseOptionalDecimal(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
