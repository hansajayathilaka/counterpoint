using Avalonia.Headless.XUnit;
using Counterpoint.Ui.Tests.Support;
using Counterpoint.Ui.Views.Catalogue;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T15's own proof that every remaining catalogue tab it converts (SRS UI-06, UI-14,
/// AC-22) passes the P3-T12 view-inspection helper - reusing
/// <see cref="ViewLabelInspector.FindInputsWithoutVisibleLabel"/> unmodified, exactly as
/// <c>ViewLabelInspectorTests</c> already proves for the Category screen and the sales side panel.
/// </summary>
public sealed class CatalogueTabViewLabelInspectorTests
{
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedBrandEditView()
    {
        AssertNoViolations(new BrandEditView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedUomEditView()
    {
        AssertNoViolations(new UomEditView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedTaxClassEditView()
    {
        AssertNoViolations(new TaxClassEditView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedSupplierEditView()
    {
        AssertNoViolations(new SupplierEditView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedProductEditView()
    {
        AssertNoViolations(new ProductEditView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedProductVariantEditView()
    {
        AssertNoViolations(new ProductVariantEditView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedProductUomOptionEditView()
    {
        AssertNoViolations(new ProductUomOptionEditView());
    }

    /// <summary>
    /// The product tab's own screen (not a dialog): its remaining free-text/numeric field - the
    /// variant-matrix generator's axes box and default-price box (SRS FR-2.6) - is not master-data
    /// create/edit/delete, so it stays on the screen rather than moving into a dialog, but every
    /// field on it must still pass the same helper.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedProductTabView()
    {
        AssertNoViolations(new ProductTabView());
    }

    /// <summary>
    /// The import tab's mapping step (SRS FR-2.22): the file-path box, the saved-profile picker
    /// and the "save mapping as" box are the free-text/combo fields this task's item 7 converts.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedImportTabView()
    {
        AssertNoViolations(new ImportTabView());
    }

    private static void AssertNoViolations(Avalonia.Controls.Control view)
    {
        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        violations.Should().BeEmpty(
            view.GetType().Name + " must have no input without a persistent, currently-visible "
            + "label. Violations: " + string.Join("; ", violations));
    }
}
