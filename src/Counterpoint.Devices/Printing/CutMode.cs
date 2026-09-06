namespace Counterpoint.Devices.Printing;

/// <summary>
/// How the auto-cutter is driven (SRS PRT-05). Which of these a given printer honours is a
/// capability, discovered on the real unit in <c>HW-T01</c> and recorded in
/// <c>docs/adr/printer-quirks.md</c>.
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
}
