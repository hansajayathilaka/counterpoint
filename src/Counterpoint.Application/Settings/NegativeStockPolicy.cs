namespace Counterpoint.Application.Settings;

/// <summary>
/// What the till does when a sale would take a balance below zero (SRS FR-10.5, Q-11).
/// </summary>
public enum NegativeStockPolicy
{
    /// <summary>
    /// Sell it anyway and let the balance go negative. The shop's answer to Q-11: a hardware
    /// counter finds stock in the back that the system does not know about, and refusing the
    /// sale would lose the customer rather than fix the count.
    /// </summary>
    Allow = 0,

    /// <summary>Sell it, but warn the cashier first.</summary>
    Warn = 1,

    /// <summary>Refuse the line.</summary>
    Block = 2,
}
