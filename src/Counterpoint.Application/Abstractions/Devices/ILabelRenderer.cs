using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a <see cref="LabelBatch"/> into the byte stream a shelf-label printer eats (SRS FR-2.10,
/// FR-2.12).
/// </summary>
/// <remarks>
/// <para>
/// Pure: no device, no file, no clock - the same contract <see cref="ISaleReceiptRenderer"/>
/// keeps for receipts. Rendering is deterministic, which is what makes the TSPL byte-stream
/// snapshot test meaningful (P1-T12 "Done when").
/// </para>
/// <para>
/// <paramref name="layout"/> (below) is read fresh from <c>ISettings.Current.Label</c> by the
/// caller on every print, not baked into the renderer at construction: a size or content change
/// in the settings screen takes effect on the very next print, without a restart (FR-10.2's same
/// promise, applied to the label layout).
/// </para>
/// <para>
/// The command set itself is TSPL (Tag Stock Programming Language), what the great majority of
/// shelf-label printers speak - not ESC/POS, which is why this is its own device abstraction and
/// not a mode of <c>EscPosRenderer</c>. <c>HW-T03</c> adjusts the command set once the shop's
/// actual model is confirmed (Q-14/Q-15).
/// </para>
/// </remarks>
public interface ILabelRenderer
{
    /// <summary>Renders one print run.</summary>
    /// <param name="batch">What to print - one entry per distinct product.</param>
    /// <param name="layout">The label's size and which fields to include.</param>
    public byte[] Render(LabelBatch batch, LabelSettings layout);
}
