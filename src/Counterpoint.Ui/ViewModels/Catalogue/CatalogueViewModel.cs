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
        CustomerTabViewModel customer)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(uom);
        ArgumentNullException.ThrowIfNull(taxClass);
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(customer);

        Category = category;
        Brand = brand;
        Uom = uom;
        TaxClass = taxClass;
        Supplier = supplier;
        Customer = customer;
    }

    public CategoryTabViewModel Category { get; }

    public BrandTabViewModel Brand { get; }

    public UomTabViewModel Uom { get; }

    public TaxClassTabViewModel TaxClass { get; }

    public SupplierTabViewModel Supplier { get; }

    public CustomerTabViewModel Customer { get; }

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
    }
}
