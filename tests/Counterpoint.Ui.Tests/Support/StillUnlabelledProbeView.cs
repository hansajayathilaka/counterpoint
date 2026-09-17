using Avalonia.Controls;

namespace Counterpoint.Ui.Tests.Support;

/// <summary>
/// A minimal probe control for <see cref="Counterpoint.Ui.Tests.ViewLabelInspectorTests"/>: one
/// bare <see cref="TextBox"/> with only a <see cref="TextBox.Watermark"/> and no
/// <c>AutomationProperties.LabeledBy</c> - the exact defect task P3-T12's
/// <see cref="ViewLabelInspector"/> exists to catch.
/// </summary>
/// <remarks>
/// Built directly in code, not AXAML: <c>Counterpoint.Ui.Tests</c> is not set up to compile its
/// own views (it only ever inspects real, already-compiled views from <c>Counterpoint.Ui</c>), and
/// this probe needs no data binding or design-time markup - it exists purely to prove
/// <see cref="ViewLabelInspector"/> still detects the defect it was built to catch once every real
/// screen this task set out to convert (P3-T11 through P3-T16) has stopped exhibiting it.
/// </remarks>
internal sealed class StillUnlabelledProbeView : UserControl
{
    public StillUnlabelledProbeView()
    {
        Content = new TextBox { Watermark = "Nothing labels this box" };
    }
}
