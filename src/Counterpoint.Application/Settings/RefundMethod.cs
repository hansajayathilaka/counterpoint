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

    /// <summary>
    /// Settled by a paired sale rather than paid out on its own (SRS FR-5 exchange, task P2-T04) -
    /// <c>sale_return.refund_method = 'EXCHANGE'</c>, only ever set by
    /// <c>Counterpoint.Application.Exchanges.CreateExchangeHandler</c>. Never valid on
    /// <see cref="Counterpoint.Application.Returns.CreateReturnCommand"/> or
    /// <see cref="Counterpoint.Application.Returns.CreateUnlinkedReturnCommand"/> - a standalone
    /// return has no paired sale to settle against (<see cref="Counterpoint.Application.Returns.RefundMethodMapping.RequireSupported"/>).
    /// </summary>
    Exchange = 3,
}
