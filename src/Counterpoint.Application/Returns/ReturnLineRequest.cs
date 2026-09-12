using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// One line of a return, as the operator entered it (SRS FR-5, task P2-T02 step 2).
/// </summary>
/// <param name="SaleLineId">The original bill line this reverses.</param>
/// <param name="QuantityBase">
/// How much is coming back, in the product's base unit - the same unit <c>sale_line.qty_base</c>
/// and <c>stock_movement.qty_base</c> already work in, so no UOM re-selection or conversion
/// happens on the way in here. Must be positive. Only <see cref="Quantity.Value"/> is read -
/// <c>CreateReturnHandler</c> re-tags it against the original line's own quantity before doing
/// any arithmetic with it, because <see cref="Quantity.UomId"/> here is
/// <c>IReturnableSaleLookup</c>'s own internal bookkeeping tag (see its remarks), not something a
/// caller building this request could know.
/// </param>
/// <param name="Disposition">
/// <c>Sellable</c> or <c>Damaged</c> - never defaulted (SRS FR-5.8). There is deliberately no
/// third value this could quietly be.
/// </param>
/// <param name="Reason">The cashier's reason for this line (SRS FR-5.7), for example "faulty".</param>
public sealed record ReturnLineRequest(
    long SaleLineId,
    Quantity QuantityBase,
    ReturnDisposition Disposition,
    string Reason);
