using Avalonia.Headless.XUnit;
using Counterpoint.Ui.Tests.Support;
using Counterpoint.Ui.Views.Settings;
using FluentAssertions;

namespace Counterpoint.Ui.Tests;

/// <summary>
/// Task P3-T15's settings-tabs half's own proof that every one of the eight pre-existing settings
/// screens (SRS UI-06, UI-14, AC-22) - the ones <c>SettingsWindow.axaml</c> already hosted before
/// task P3-T10 added a ninth, Display, which was built onto the token system from the start and is
/// not part of this task's scope - passes the P3-T12 view-inspection helper, reusing
/// <see cref="ViewLabelInspector.FindInputsWithoutVisibleLabel"/> unmodified, exactly as
/// <c>CatalogueTabViewLabelInspectorTests</c> already proves for the catalogue-tabs half.
/// </summary>
public sealed class SettingsTabViewLabelInspectorTests
{
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedShopProfileSettingsView()
    {
        AssertNoViolations(new ShopProfileSettingsView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedFinancialSettingsView()
    {
        AssertNoViolations(new FinancialSettingsView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedTaxSettingsView()
    {
        AssertNoViolations(new TaxSettingsView());
    }

    /// <summary>
    /// <see cref="NumberingSettingsView"/> itself declares no field directly - it hosts an
    /// <c>ItemsControl</c> of <see cref="DocumentNumberingView"/>, one per document series - so
    /// this proves the screen has nothing unlabelled of its own, and
    /// <see cref="UI_14_TheHelperPassesAgainstTheConvertedDocumentNumberingView"/> proves the
    /// fields that actually appear in it, per series.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedNumberingSettingsView()
    {
        AssertNoViolations(new NumberingSettingsView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedDocumentNumberingView()
    {
        AssertNoViolations(new DocumentNumberingView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedPolicySettingsView()
    {
        AssertNoViolations(new PolicySettingsView());
    }

    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedPeripheralSettingsView()
    {
        AssertNoViolations(new PeripheralSettingsView());
    }

    /// <summary>
    /// Includes the two passphrase boxes, which are not built from the P3-T12 <c>LabeledField</c>
    /// family (they need <c>PasswordChar</c> masking, which the family does not offer) but still
    /// wire <c>AutomationProperties.LabeledBy</c> in the code-behind, the same way
    /// <c>ImportTabView</c>/<c>ProductTabView</c> do for a field the family cannot express.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedBackupSettingsView()
    {
        AssertNoViolations(new BackupSettingsView());
    }

    /// <summary>
    /// Includes the return-policy paragraph and the bill layout template, both plain multi-line
    /// <c>TextBox</c>es wired to <c>AutomationProperties.LabeledBy</c> in the code-behind for the
    /// same reason as the two passphrase boxes above.
    /// </summary>
    [AvaloniaFact]
    public void UI_14_TheHelperPassesAgainstTheConvertedReceiptSettingsView()
    {
        AssertNoViolations(new ReceiptSettingsView());
    }

    private static void AssertNoViolations(Avalonia.Controls.Control view)
    {
        var violations = ViewLabelInspector.FindInputsWithoutVisibleLabel(view);

        violations.Should().BeEmpty(
            view.GetType().Name + " must have no input without a persistent, currently-visible "
            + "label. Violations: " + string.Join("; ", violations));
    }
}
