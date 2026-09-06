namespace Counterpoint.Devices.Printing;

/// <summary>
/// Which pin of the printer's RJ11/RJ12 kick-out port the cash drawer is wired to
/// (SRS FR-7.7, §14.2). The values are the <c>m</c> of <c>ESC p m t1 t2</c>.
/// </summary>
public enum DrawerPin
{
    /// <summary>Pin 2. The common wiring, and the default.</summary>
    Pin2 = 0,

    /// <summary>Pin 5. Used by some drawer/printer combinations.</summary>
    Pin5 = 1,
}
