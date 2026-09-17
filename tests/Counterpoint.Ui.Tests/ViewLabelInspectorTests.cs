using Avalonia.Headless.XUnit;
using Counterpoint.Ui.Tests.Support;
using Counterpoint.Ui.Views;
using Counterpoint.Ui.Views.Catalogue;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T12's done-when proof that the view-inspection helper actually detects the defect it
/// is meant to catch (SRS UI-06, UI-14, AC-22), rather than being a tautology that would pass
/// against anything: originally (before task P3-T15 converted it) this proved the helper failed
/// against the still-unconverted <see cref="CustomerTabView"/>, whose name/phone/address/tax-no/
/// credit-limit fields used <c>Watermark</c> as their only label. Task P3-T15 converted that
/// screen's create/edit dialog content onto the P3-T12 <c>LabeledField</c> family
/// (<see cref="CustomerEditView"/>), the same way it did for every other remaining catalogue tab,
/// so the "still fails" half of the proof now lives on a different, still-genuinely-unconverted
/// control (<see cref="StillUnlabelledProbeView"/>) instead - the helper must keep detecting the
/// defect somewhere, or this test suite would have quietly stopped proving anything.
/// </summary>
public sealed class ViewLabelInspectorTests
{
    [AvaloniaFact]
    public void UI_14_TheHelperFailsAgainstAGenuinelyUnlabelledWatermarkOnlyField()
    {
        var view = new StillUnlabelledProbeView();

        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        // A bare TextBox with only a Watermark, and no AutomationProperties.LabeledBy, is exactly
        // the defect this helper exists to catch - proving it actually detects the defect, not
        // just a tautology that would pass against anything.
        violations.Should().NotBeEmpty(
            "a TextBox with only a Watermark and no AutomationProperties.LabeledBy is the exact "
            + "defect the helper exists to catch");
    }

    /// <summary>
    /// Task P3-T15's own proof that the screen this task converted (SRS UI-06, UI-14, AC-22) now
    /// passes the same helper that used to fail against it: <see cref="CustomerTabView"/>'s old
    /// inline form - five watermark-only fields (name, phone, address, tax number, credit limit)
    /// - is gone, replaced by <see cref="CustomerEditView"/>, built entirely from the P3-T12
    /// <c>LabeledField</c> family.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheNowConvertedCustomerEditView()
    {
        var view = new CustomerEditView();

        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        violations.Should().BeEmpty(
            "CustomerEditView is built entirely from the P3-T12 LabeledField family, whose "
            + "persistent label is wired via AutomationProperties.LabeledBy - the helper must "
            + "find nothing to report. Violations: " + string.Join("; ", violations));
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedCategoryEditView()
    {
        var view = new CategoryEditView();

        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        violations.Should().BeEmpty(
            "CategoryEditView is built entirely from the P3-T12 LabeledField family, whose "
            + "persistent label is wired via AutomationProperties.LabeledBy - the helper must "
            + "find nothing to report. Violations: " + string.Join("; ", violations));
    }

    /// <summary>
    /// Task P3-T14's own proof: every side-panel field the sales screen retrofit converts (search,
    /// discount, hold label, customer, opening float, open item, and all four tender boxes) is
    /// built from the same P3-T12 LabeledField family, so the helper finds nothing to report here
    /// either - the same "not a tautology" argument <see cref="UI_14_TheHelperPassesAgainstTheConvertedCategoryEditView"/>
    /// makes for the Category screen.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedSalesSidePanelView()
    {
        var view = new SalesSidePanelView();

        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        violations.Should().BeEmpty(
            "SalesSidePanelView is built entirely from the P3-T12 LabeledField family - the "
            + "helper must find nothing to report. Violations: " + string.Join("; ", violations));
    }
}
