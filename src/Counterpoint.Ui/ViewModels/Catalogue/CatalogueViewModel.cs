using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The owner's catalogue reference-data screen: category, brand, unit, tax class, supplier and
/// customer, one tab each (SRS FR-2.20, FR-2.21, FR-6).
/// </summary>
/// <remarks>
/// Six tabs sharing one window rather than six windows: this is administration a shop visits
/// rarely and briefly, not a workflow with its own screen real estate to defend, and the
/// Application layer's <c>[RequiresRole(Role.Owner)]</c> on every one of the six maintenance
/// interfaces is what actually keeps a cashier out - this window being reachable at all is not
/// the control (SRS NFR-S2, AC-17).
/// </remarks>
public sealed partial class CatalogueViewModel : ViewModelBase
{
    public CatalogueViewModel(
        CategoryTabViewModel category,
        BrandTabViewModel brand,
        UomTabViewModel uom,
        TaxClassTabViewModel taxClass,
        SupplierTabViewModel supplier,
        CustomerTabViewModel customer,
        ProductTabViewModel product,
        ImportTabViewModel import)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(uom);
        ArgumentNullException.ThrowIfNull(taxClass);
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(import);

        Category = category;
        Brand = brand;
        Uom = uom;
        TaxClass = taxClass;
        Supplier = supplier;
        Customer = customer;
        Product = product;
        Import = import;
    }

    public CategoryTabViewModel Category { get; }

    public BrandTabViewModel Brand { get; }

    public UomTabViewModel Uom { get; }

    public TaxClassTabViewModel TaxClass { get; }

    public SupplierTabViewModel Supplier { get; }

    public CustomerTabViewModel Customer { get; }

    /// <summary>P1-T05: products, variants and units (SRS FR-2.1-FR-2.8, FR-3.6, AC-08).</summary>
    public ProductTabViewModel Product { get; }

    /// <summary>P1-T13: spreadsheet catalogue import and export (SRS FR-2.22, FR-2.23, AC-07, Q-08).</summary>
    public ImportTabViewModel Import { get; }

    /// <summary>Loads every tab. Called once, when the window opens.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await Category.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await Brand.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await Uom.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await TaxClass.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await Supplier.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await Customer.RefreshAsync(cancellationToken).ConfigureAwait(true);

        // Last, deliberately: Product.RefreshAsync populates the category/brand/unit/tax-class
        // pickers from the same lists the tabs above just loaded, so it reads them in a state
        // that already reflects anything those tabs seeded.
        await Product.RefreshAsync(cancellationToken).ConfigureAwait(true);

        // The import tab needs only its saved mapping profiles - it has no reference-data list of
        // its own to load.
        await Import.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }
}
