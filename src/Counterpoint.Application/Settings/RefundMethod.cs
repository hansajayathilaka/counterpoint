namespace Counterpoint.Application.Settings;

/// <summary>How a return is refunded when the cashier does not choose otherwise (SRS FR-10.5).</summary>
public enum RefundMethod
{
    /// <summary>Cash out of the drawer.</summary>
    Cash = 0,

    /// <summary>A credit note the customer spends later.</summary>
    CreditNote = 1,

    /// <summary>Back to the card the sale was paid with.</summary>
    Card = 2,
}
