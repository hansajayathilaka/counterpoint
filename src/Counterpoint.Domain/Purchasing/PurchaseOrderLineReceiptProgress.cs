using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Purchasing;

/// <summary>
/// One purchase order line's ordered quantity against what has been received so far, both in the
/// line's product's base unit - what <see cref="PurchaseOrderStatusCalculator"/> compares
/// (docs/01_DATA_MODEL.md §4, SRS FR-4.10).
/// </summary>
/// <param name="OrderedBase">
/// <c>purchase_order_line.qty</c> converted into the product's base unit - the ordering unit and
/// the conversion factor are the caller's business (<c>Counterpoint.Domain.Catalogue.UomConverter</c>);
/// this type only ever sees the two figures in the unit they are compared in.
/// </param>
/// <param name="ReceivedBase"><c>purchase_order_line.qty_received_base</c>, as it stands now.</param>
public sealed record PurchaseOrderLineReceiptProgress(Quantity OrderedBase, Quantity ReceivedBase);
