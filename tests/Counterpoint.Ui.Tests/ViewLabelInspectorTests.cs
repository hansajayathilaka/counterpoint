using Avalonia.Headless.XUnit;
using Counterpoint.Ui.Tests.Support;
using Counterpoint.Ui.Views.Catalogue;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T12's done-when proof that the view-inspection helper actually detects the defect it
/// is meant to catch (SRS UI-06, UI-14, AC-22), rather than being a tautology that would pass
/// against anything: it fails against the still-unconverted <see cref="CustomerTabView"/> (whose
/// name/phone/address/tax-no/credit-limit fields use <c>Watermark</c> as their only label - task
/// P3-T15's job, not this one's), and it passes against the Category screen's edit form
/// (<see cref="CategoryEditView"/>), which this task did convert to the P3-T12
/// <c>LabeledField</c> family.
/// </summary>
public sealed class ViewLabelInspectorTests
{
    [AvaloniaFact]
    public void UI_14_TheHelperFailsAgainstTheStillUnconvertedCustomerTabView()
    {
        var view = new CustomerTabView();

        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        // CustomerTabView.axaml still has five watermark-only fields (name, phone, address,
        // tax number, credit limit) - P3-T15 converts this screen, not this task.
        violations.Should().NotBeEmpty(
            "CustomerTabView's fields still use Watermark as their only label, so the helper "
            + "must flag them - proving it actually detects the defect, not just a tautology");
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
}
