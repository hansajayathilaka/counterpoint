using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// Prices a linked return's lines against the original bill: the cumulative over-return guard
/// (AC-06) and the non-returnable check (AC-05) first, then the refund itself, prorated from the
/// original line's own totals so a partial return of a discounted line refunds exactly its share
/// of the discount (AC-03) - never <see cref="ReturnableSaleLine.UnitPrice"/> multiplied fresh by
/// the requested quantity, which would silently drop it.
/// </summary>
/// <remarks>
/// Extracted from <see cref="CreateReturnHandler"/> (task P2-T02) so
/// <see cref="Counterpoint.Application.Exchanges.CreateExchangeHandler"/> (task P2-T04) can price
/// the return half of an exchange by exactly the same rule a standalone linked return uses,
/// without either flow calling the other as a black box - the same extraction shape task P2-T03
/// used for <see cref="RefundMethodMapping"/> and <see cref="ReturnPolicyTextBuilder"/>.
/// </remarks>
internal static class ReturnPricer
{
    /// <summary>Prices every requested line, checked and ready to write.</summary>
    /// <param name="sale">The original bill, reassembled fresh (never a cached read).</param>
    /// <param name="lines">What the cashier asked to return.</param>
    /// <param name="policy">
    /// The return policy authorisation service - AC-06 and AC-05 are both re-checked here, per
    /// line, the same guard whichever caller is pricing.
    /// </param>
    /// <param name="nonReturnableOverride">
    /// The override token for a non-returnable line, if one was obtained. One token authorises one
    /// line, exactly as <see cref="CreateReturnHandler"/>'s own remarks describe.
    /// </param>
    /// <param name="rounding">The line-total rounding point (CLAUDE.md invariant 2).</param>
    /// <param name="restockingFeeRate">The policy's restocking fee rate, applied to the merchandise value alone.</param>
    public static PricedReturn Price(
        ReturnableSale sale,
        IReadOnlyList<ReturnLineRequest> lines,
        IReturnPolicyAuthorisationService policy,
        OverrideToken? nonReturnableOverride,
        IRoundingPolicy rounding,
        Percentage restockingFeeRate)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rounding);

        var linesBySaleLineId = sale.Lines.ToDictionary(line => line.SaleLineId);

        var priced = new List<PricedReturnLine>(lines.Count);
        var subtotal = Money.Zero;
        var tax = Money.Zero;

        foreach (var request in lines)
        {
            if (!linesBySaleLineId.TryGetValue(request.SaleLineId, out var original))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bill {sale.BillNo} has no line {request.SaleLineId}."));
            }

            if (!request.QuantityBase.IsPositive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lines), request.QuantityBase.Value, "A return line must ask for a positive quantity.");
            }

            // Re-tagged against the original line's own quantities, not trusted from the caller -
            // see ReturnLineRequest's own remarks.
            var requestedQuantity = Quantity.FromDecimal(request.QuantityBase.Value, original.QtySoldBase.UomId);

            // AC-06, never overridable.
            policy.AuthoriseCumulativeQuantity(original.QtySoldBase, original.QtyReturnedBase, requestedQuantity);

            // AC-05: a non-returnable product or category needs an owner override; a returnable
            // one sails through untouched.
            policy.AuthoriseNonReturnable(original.NonReturnable, original.CategoryId, nonReturnableOverride);

            var ratio = requestedQuantity.Value / original.QtySoldBase.Value;

            // Rounding point one, for this document: the line's own refund (mirrors
            // sale_line.line_total's own rounding when the bill was completed).
            var lineRefund = rounding.Round(original.LineTotal * ratio);

            // Quantised to the storage scale only, exactly as CompleteSaleHandler leaves a line's
            // tax - not an independent rounding point (CLAUDE.md invariant 2).
            var lineTax = Money.FromScaled((original.Tax * ratio).ToScaled());

            subtotal += lineRefund;
            tax += lineTax;

            priced.Add(new PricedReturnLine(
                original.SaleLineId,
                original.ProductVariantId,
                original.Description,
                original.UomSymbol,
                requestedQuantity,
                original.UnitPrice,
                original.UnitCost,
                lineTax,
                lineRefund,
                request.Reason,
                request.Disposition));
        }

        // The one restocking-fee accessor the policy engine offers, applied to the merchandise
        // value alone - never to tax.
        var fee = rounding.Round(restockingFeeRate.Of(subtotal));

        // Exactly the sum of already-rounded/quantised parts, not a further rounding point:
        // sale_return carries no residual "rounding" column the way sale does, so this identity
        // has to hold by construction rather than by a third rounding call papering over it.
        var totalRefund = subtotal + tax - fee;

        return new PricedReturn(subtotal, tax, fee, totalRefund, priced);
    }
}

/// <summary>A return, priced and checked, ready to be written (task P2-T02, shared per <see cref="ReturnPricer"/>).</summary>
internal sealed record PricedReturn(
    Money Subtotal,
    Money Tax,
    Money RestockingFee,
    Money TotalRefund,
    IReadOnlyList<PricedReturnLine> Lines)
{
    /// <summary>Builds the return receipt's own lines and totals - the caller supplies everything about the document itself.</summary>
    internal SaleReturnReceipt ToReceipt(
        string returnNo,
        string originalBillNo,
        DateTimeOffset returnedAt,
        string refundMethodToken,
        string cashierName,
        string policyText) => new(
            returnNo,
            originalBillNo,
            returnedAt,
            [.. Lines.Select(line => line.ToReceiptLine())],
            Subtotal,
            Tax,
            RestockingFee,
            TotalRefund,
            refundMethodToken,
            cashierName,
            policyText);
}

/// <summary>One priced return line (task P2-T02, shared per <see cref="ReturnPricer"/>).</summary>
internal sealed record PricedReturnLine(
    long SaleLineId,
    long? ProductVariantId,
    string Description,
    string UomSymbol,
    Quantity QuantityBase,
    Money UnitPrice,
    Money UnitCost,
    Money Tax,
    Money LineRefund,
    string Reason,
    ReturnDisposition Disposition)
{
    internal NewSaleReturnLine ToNewSaleReturnLine() => new(
        SaleLineId,
        ProductVariantId ?? throw new InvalidOperationException(
            "An open-item line cannot be returned to stock; P2-T02 does not price one - " +
            "sale_return_line.product_variant_id is NOT NULL in the schema."),
        QuantityBase,
        UnitPrice,
        UnitCost,
        Tax,
        LineRefund,
        Reason,
        ReturnDispositions.ToToken(Disposition));

    internal SaleReturnReceiptLine ToReceiptLine() => new(
        Description, QuantityBase, UomSymbol, UnitPrice, LineRefund, ReturnDispositions.ToToken(Disposition));
}
