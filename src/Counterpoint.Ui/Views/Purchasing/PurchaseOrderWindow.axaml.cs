using Avalonia.Automation;
using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Purchasing;

/// <summary>
/// The owner's purchase-order screen. Every behaviour it has is a binding to
/// <see cref="ViewModels.Purchasing.PurchaseOrderViewModel"/> (SRS FR-4.5, FR-4.6, FR-4.10), except
/// one piece of task P3-T16 wiring (SRS UI-14, AC-22): the supplier picker needs an
/// <c>ItemTemplate</c> to show a name rather than a raw row's <c>ToString()</c>, which the P3-T12
/// <c>LabeledComboField</c> does not offer, so it stays a plain <see cref="ComboBox"/>/
/// <see cref="TextBlock"/> pair wired to <c>AutomationProperties.LabeledBy</c> here - the same
/// accepted fallback <see cref="Counterpoint.Ui.Views.Catalogue.ImportTabView"/>/
/// <see cref="Counterpoint.Ui.Views.Catalogue.ProductTabView"/>/
/// <see cref="Counterpoint.Ui.Views.Settings.BackupSettingsView"/> already use for a field the
/// family cannot express.
/// </summary>
public partial class PurchaseOrderWindow : Window
{
    public PurchaseOrderWindow()
    {
        InitializeComponent();

        var supplierCombo = this.FindControl<ComboBox>("SupplierCombo");
        var supplierLabel = this.FindControl<TextBlock>("SupplierLabel");

        if (supplierCombo is not null && supplierLabel is not null)
        {
            AutomationProperties.SetLabeledBy(supplierCombo, supplierLabel);
        }
    }
}
