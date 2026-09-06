namespace Counterpoint.Devices.Printing;

/// <summary>
/// Which cut command is sent (SRS PRT-05). Which of these a given printer honours is a
/// capability, discovered on the real unit in <c>HW-T01</c> and recorded in
/// <c>docs/adr/printer-quirks.md</c>.
///
/// <para>
/// This selects a whole command, not a parameter of one: <c>GS V</c> is the Epson family that
/// most units speak, but Star, Bixolon and a good number of generic 80 mm boards cut with the
/// older <c>ESC i</c> / <c>ESC m</c> and ignore <c>GS V</c> entirely. Meeting one of those is
/// a settings change.
/// </para>
///
/// <para>
/// The numeric values of the <c>GS V</c> members happen to be that command's <c>m</c> byte;
/// nothing depends on it - <see cref="EscPos.Cut"/> writes the bytes for each member out in
/// full.
/// </para>
/// </summary>
public enum CutMode
{
    /// <summary>Partial cut, leaving a small tab: <c>GS V 1</c>. The usual choice.</summary>
    Partial = 1,

    /// <summary>Full cut: <c>GS V 0</c>.</summary>
    Full = 0,

    /// <summary>Feed the configured lines and then partially cut, in one command: <c>GS V 66 n</c>.</summary>
    FeedAndPartial = 66,

    /// <summary>Feed the configured lines and then fully cut, in one command: <c>GS V 65 n</c>.</summary>
    FeedAndFull = 65,

    /// <summary>
    /// Full cut with the older two-byte command: <c>ESC i</c>. For a printer that claims
    /// ESC/POS but does nothing at all when sent <c>GS V</c>.
    /// </summary>
    EscFullCut = 105,

    /// <summary>Partial cut with the older two-byte command: <c>ESC m</c>.</summary>
    EscPartialCut = 109,
}
