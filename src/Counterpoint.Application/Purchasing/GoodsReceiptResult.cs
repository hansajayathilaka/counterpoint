using System.Collections.Generic;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Purchasing;

/// <summary>
/// One line whose landed cost on this receipt exceeds the variant's current selling price (SRS
/// FR-2.18, FR-4.8: "optionally prompt to update selling prices where cost has risen").
/// </summary>
/// <remarks>
/// A flag, not a block: the receipt has already posted by the time
/// <see cref="IGoodsReceiptService.ReceiveAsync"/> returns this. FR-4.8 reads "prompt to update
/// selling prices ... showing the resulting margin before the owner confirms" - the confirming
/// and the price change themselves are a UI concern and a separate call into
/// <c>Counterpoint.Application.Catalogue.IProductMaintenance.UpdateVariantAsync</c>, both outside
/// this task (P2-T07's own scope is making the condition detectable, not building the dialog).
/// </remarks>
/// <param name="ProductVariantId">The variant whose cost rose above its own price.</param>
/// <param name="Sku">The variant's SKU, for the prompt.</param>
/// <param name="Description">The product name, for the prompt.</param>
/// <param name="LandedUnitCostBase">The new landed cost per base unit, from this receipt.</param>
/// <param name="CurrentSellingPrice">The variant's selling price per base unit, before any change.</param>
public sealed record GoodsReceiptPriceReviewFlag(
    long ProductVariantId,
    string Sku,
    string Description,
    Money LandedUnitCostBase,
    Money CurrentSellingPrice);

/// <summary>
/// What <see cref="IGoodsReceiptService.ReceiveAsync"/> hands back: the receipt as written, which
/// lines need a price review, and how the shelf-label batch for the received stock went.
/// </summary>
/// <param name="Receipt">The goods receipt, as it was written.</param>
/// <param name="PriceReviewFlags">Every line whose landed cost now exceeds its selling price (SRS FR-2.18, FR-4.8). Empty when none do.</param>
/// <param name="LabelPrintOutcome">
/// What happened when the shelf-label batch for the received quantities was sent to the label
/// printer (SRS FR-2.12, P1-T12). A printer that is out of ribbon or unplugged does not undo the
/// receipt (CLAUDE.md invariant 7) - the caller shows this as a warning, nothing more.
/// </param>
public sealed record GoodsReceiptResult(
    GoodsReceiptRecord Receipt,
    IReadOnlyList<GoodsReceiptPriceReviewFlag> PriceReviewFlags,
    PrintOutcome LabelPrintOutcome);
