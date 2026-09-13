using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Exchanges;

/// <summary>What <see cref="ICreateExchange.CreateAsync"/> hands back once the exchange has committed.</summary>
/// <param name="SaleReturnId">The new <c>sale_return</c> row - what came back.</param>
/// <param name="ReturnNo">The allocated return number.</param>
/// <param name="SaleId">The new <c>sale</c> row - what went out. Cross-linked to <paramref name="SaleReturnId"/> via <c>sale_return.exchange_sale_id</c>.</param>
/// <param name="BillNo">The allocated bill number.</param>
/// <param name="ReturnValue">What the returned goods were worth (net of any restocking fee).</param>
/// <param name="ReplacementTotal">What the replacement goods were worth, before the credit was applied.</param>
/// <param name="CreditApplied">How much of <paramref name="ReturnValue"/> was applied against <paramref name="ReplacementTotal"/>.</param>
/// <param name="AmountCollected">What was actually tendered for a higher-priced replacement. Zero otherwise.</param>
/// <param name="Change">What was handed back on an over-tender of <paramref name="AmountCollected"/>.</param>
/// <param name="RefundPaid">What was paid back for real for a lower-priced replacement's surplus. Zero otherwise.</param>
/// <param name="PrintJobId">The queued combined receipt - never printed here (CLAUDE.md invariant 7).</param>
public sealed record CreatedExchange(
    long SaleReturnId,
    string ReturnNo,
    long SaleId,
    string BillNo,
    Money ReturnValue,
    Money ReplacementTotal,
    Money CreditApplied,
    Money AmountCollected,
    Money Change,
    Money RefundPaid,
    long PrintJobId);
