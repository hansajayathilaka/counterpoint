using Avalonia.Automation;
using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The product tab. Markup and one small piece of behaviour the P3-T12 <c>LabeledField</c> family
/// cannot express itself: the variant-matrix axes box (SRS FR-2.6) needs a multi-line, "one axis
/// per line" text area, which <c>LabeledTextField</c> does not offer (it is a single-line box).
/// Everything else on this view is a binding to
/// <see cref="ViewModels.Catalogue.ProductTabViewModel"/>.
/// </summary>
public partial class ProductTabView : UserControl
{
    public ProductTabView()
    {
        InitializeComponent();

        // Wires the same AutomationProperties.LabeledBy association LabeledTextField's own
        // constructor sets up (task P3-T12, SRS UI-14) - what the view-inspection helper checks
        // for - onto a plain TextBox/TextBlock pair, since this one field needs AcceptsReturn.
        var axesBox = this.FindControl<TextBox>("MatrixAxesBox");
        var axesLabel = this.FindControl<TextBlock>("MatrixAxesLabel");
        if (axesBox is not null && axesLabel is not null)
        {
            AutomationProperties.SetLabeledBy(axesBox, axesLabel);
        }
    }
}
