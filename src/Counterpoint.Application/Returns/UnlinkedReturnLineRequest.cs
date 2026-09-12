using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// One line of a return taken with no original bill (SRS FR-5.19, task P2-T03 step 3).
/// </summary>
/// <param name="ProductVariantId">
/// The item being returned, identified straight from the catalogue rather than from a bill line -
/// there is no <c>sale_line</c> to point at (<c>sale_return_line.sale_line_id IS NULL</c>,
/// docs/01_DATA_MODEL.md §6). <c>sale_return_line.product_variant_id</c> is <c>NOT NULL</c> even
/// here, so an open-item (non-catalogue) unlinked return line is not something this schema can
/// express.
/// </param>
/// <param name="QuantityBase">
/// How much is coming back, in the product's base unit - the same convention
/// <see cref="ReturnLineRequest.QuantityBase"/> uses for a linked return. Must be positive.
/// </param>
/// <param name="UnitPrice">
/// What the operator is refunding, per base unit. Must be positive and no more than the
/// product's <em>current</em> selling price at the moment the return is taken - the handler caps
/// it there and refuses anything higher (task P2-T03 step 3: "the operator cannot type a higher
/// figure"). A screen would default this field to that same current price and let the operator
/// type a lower one (a goodwill part-refund, say); there is deliberately no "original price" to
/// default to instead, because there is no original sale.
/// </param>
/// <param name="Disposition">
/// <c>Sellable</c> or <c>Damaged</c> (SRS FR-5.8), exactly as a linked return line - never
/// defaulted.
/// </param>
/// <param name="Reason">The cashier's reason for this line (SRS FR-5.7).</param>
public sealed record UnlinkedReturnLineRequest(
    long ProductVariantId,
    Quantity QuantityBase,
    Money UnitPrice,
    ReturnDisposition Disposition,
    string Reason);
