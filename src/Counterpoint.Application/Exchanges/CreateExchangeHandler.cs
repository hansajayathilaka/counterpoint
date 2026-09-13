using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Pricing;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Sales;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Exchanges;

/// <summary>
/// The exchange commit: one <c>sale_return</c> and one <c>sale</c>, cross-linked, in one
/// transaction (SRS FR-5 exchange, AC-04, task P2-T04):
///
/// <code>
/// BEGIN IMMEDIATE
///   allocate bill_no from number_sequence          -- the sale must exist before the return can
///   insert sale (+ prev_hash / row_hash)               reference it (exchange_sale_id FK)
///   insert sale_line[]
///   insert payment[]                                -- only when the replacement costs more
///   post SALE stock movements (outbound)
///   allocate return_no from number_sequence
///   insert sale_return (+ prev_hash / row_hash), exchange_sale_id = the sale just inserted
///   insert sale_return_line[]
///   increment sale_line.qty_returned[]              -- the one permitted update, guard checked first
///   post RETURN_IN stock movements                  -- SELLABLE lines only
///   insert refund payment                           -- only when the return is worth more
///   insert audit_log x2 (sale, sale_return)
///   insert print_job                                -- one row, outbox, not a printer call
/// COMMIT
/// </code>
///
/// If anything in that block throws, nothing happened except two consumed document numbers -
/// correct and auditable, the same as a failed sale or a failed standalone return.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuses, never calls, the two flows it sits between.</b> The return half is priced by
/// <see cref="ReturnPricer"/> - the exact function <see cref="CreateReturnHandler"/> itself calls,
/// extracted for this reason - and every return-policy check goes through the same
/// <see cref="IReturnPolicyAuthorisationService"/> a standalone return uses. The replacement half
/// is priced here directly against the same primitives <c>CompleteSaleHandler</c> uses
/// (<see cref="IProductLookup"/>, <c>UomConverter</c>, <see cref="IDiscountAuthorisationService"/>,
/// <see cref="IRoundingPolicy"/>) rather than by calling <c>CompleteSaleHandler.CompleteAsync</c>
/// or its private pricing methods - that handler opens its own transaction and its per-line
/// pricing is not a reusable public surface, so this duplicates a smaller version of it instead of
/// restructuring it. It also duplicates <c>CompleteSaleHandler.PriceAsync</c>'s own negative-stock
/// fix (P1-T09): a <c>claimedByVariant</c> accumulator, threaded through every replacement line's
/// pricing call, so two replacement lines of the same variant in one exchange are checked (and,
/// for Warn, report their message) against on-hand net of what earlier lines in this same
/// exchange already claimed - not the catalogue's raw on-hand figure independently per line.
/// </para>
/// <para>
/// <b>The credit is <c>sale.bill_discount</c>, not a promotional discount.</b> <c>sale</c> has no
/// column for "value settled by a paired return" and none is added here (no migration is needed
/// for this task) - <c>bill_discount</c> is the one column whose entire purpose is "the difference
/// between subtotal+tax and total that is not tax or rounding", so
/// <see cref="Domain.Returns.ExchangeSettlementResult.CreditApplied"/> is written there directly,
/// bypassing <see cref="IDiscountAuthorisationService.AuthoriseBillDiscount"/> and its cashier
/// discount cap entirely - this is not a discount a cashier is granting, it is the value of goods
/// already handed back. Two consequences are accepted, deliberately, as an interim design: a
/// Phase 3 report that totals "discounts given" would need to exclude exchange-linked sales (found
/// via <c>sale_return.exchange_sale_id</c>) to avoid overstating discounting; and reprinting this
/// sale's receipt in isolation later, through the ordinary <c>ISaleReceiptRenderer</c>/
/// <c>IReprintReceipt</c> path, shows the credit as an undifferentiated "Discount" line rather than
/// "Applied from return" - only the <see cref="ExchangeReceipt"/> printed at the time of the
/// exchange, through <see cref="IExchangeReceiptRenderer"/>, labels it correctly. Both are
/// documented follow-ups for whichever task first needs the distinction (a dedicated
/// <c>sale.exchange_credit</c> column would resolve both, but that is a schema change this task's
/// brief does not call for).
/// </para>
/// <para>
/// <b>Why not a <c>payment</c> row for the credit instead.</b> <c>payment.tender_type</c>'s CHECK
/// constraint has no <c>'EXCHANGE'</c> token (only <c>sale_return.refund_method</c> does), and
/// every token it does have already means something else a report depends on:
/// <c>CREDIT_NOTE</c> implies a redeemable <c>credit_note</c> row (P2-T05, which does not exist),
/// <c>ON_ACCOUNT</c> implies a credit-customer balance (P5-T02, which does not exist), and misusing
/// <c>CASH</c>/<c>CARD</c> for a movement that never happened at the drawer or the terminal would
/// corrupt a future till-reconciliation report. <c>bill_discount</c>, despite the labelling
/// consequence above, pollutes nothing that does not already know to expect it.
/// </para>
/// <para>
/// <b>Only one side ever collects or refunds anything.</b>
/// <see cref="Domain.Returns.ExchangeSettlement.Calculate"/> guarantees
/// <see cref="Domain.Returns.ExchangeSettlementResult.AmountOwed"/> and
/// <see cref="Domain.Returns.ExchangeSettlementResult.LeftoverRefund"/> are never both positive, so
/// this handler never writes both a sale payment and a return refund payment for the same
/// exchange - AC-04's "higher-priced replacement collects the correct difference" and "a
/// lower-priced replacement refunds... correctly" are two branches of one settlement, not two
/// independent amounts that could drift apart.
/// </para>
/// <para>
/// <b>Daily totals reconcile without a report knowing anything special.</b> <c>sale.total</c> for
/// the replacement is already net of the credit, so summing <c>sale.total</c> across a day already
/// counts an exchange's net cash impact, not its full merchandise value twice; <c>sale.subtotal</c>
/// still shows the full merchandise value, which is what a stock/COGS view wants. The return posts
/// a refund <c>payment</c> row only for a real <see cref="Domain.Returns.ExchangeSettlementResult.LeftoverRefund"/> -
/// never for the credited portion - so summing refund payments never double-counts the part that
/// went into the paired sale instead. <c>exchange_sale_id</c> itself is what lets a future report
/// (Phase 3) explicitly net the pair, per the phase doc's own risk note, without needing any of
/// this arithmetic explained to it first.
/// </para>
/// </remarks>
public sealed class CreateExchangeHandler : ICreateExchange
{
    private const string SaleDocumentType = "SALE";
    private const string ReturnDocumentType = "RETURN";
    private const string SaleMovementType = "SALE";
    private const string ReturnInMovementType = "RETURN_IN";
    private const string NegativeStockAuditAction = "NEGATIVE_STOCK_SALE";
    private const string SaleAuditAction = "SALE_COMPLETED";
    private const string ReturnAuditAction = "RETURN_COMPLETED";
    private const string CompletedSaleStatus = "COMPLETED";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IReturnableSaleLookup _returnableSales;
    private readonly IProductLookup _catalogue;
    private readonly ISaleWriter _sales;
    private readonly ISaleReturnWriter _returns;
    private readonly IStockLedger _stock;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly IExchangeReceiptRenderer _receipts;
    private readonly IRoundingPolicy _rounding;
    private readonly ISession _session;
    private readonly IReturnPolicyAuthorisationService _returnPolicy;
    private readonly IDiscountAuthorisationService _discounts;
    private readonly ISettings _settings;
    private readonly ICategoryStore _categories;

    public CreateExchangeHandler(
        IUnitOfWork unitOfWork,
        IDocumentNumberAllocator numbers,
        IReturnableSaleLookup returnableSales,
        IProductLookup catalogue,
        ISaleWriter sales,
        ISaleReturnWriter returns,
        IStockLedger stock,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        IExchangeReceiptRenderer receipts,
        IRoundingPolicy rounding,
        ISession session,
        IReturnPolicyAuthorisationService returnPolicy,
        IDiscountAuthorisationService discounts,
        ISettings settings,
        ICategoryStore categories)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(returnableSales);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(rounding);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(returnPolicy);
        ArgumentNullException.ThrowIfNull(discounts);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(categories);

        _unitOfWork = unitOfWork;
        _numbers = numbers;
        _returnableSales = returnableSales;
        _catalogue = catalogue;
        _sales = sales;
        _returns = returns;
        _stock = stock;
        _audit = audit;
        _printJobs = printJobs;
        _receipts = receipts;
        _rounding = rounding;
        _session = session;
        _returnPolicy = returnPolicy;
        _discounts = discounts;
        _settings = settings;
        _categories = categories;
    }

    /// <inheritdoc />
    public async Task<CreatedExchange> CreateAsync(
        CreateExchangeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAtLeastOneReturnLine(command);
        RequireNoDuplicateReturnLines(command);
        RequireCashOrCardOnly(command.ExcessRefundMethod);

        var seller = RequireTheSellerIsSignedIn(command);

        // Read before the transaction opens - the same NFR-P3 discipline CreateReturnHandler and
        // CompleteSaleHandler each keep for their own reads: the writer lock is for the write alone.
        var sale = await _returnableSales.FindBySaleIdAsync(command.OriginalSaleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {command.OriginalSaleId} was not found."));

        RequireSaleIsReturnable(sale);

        _returnPolicy.AuthoriseReturnWindow(sale.SoldAt, command.ReturnWindowOverride);
        _returnPolicy.AuthoriseReceiptRequirement(command.BillReferencePresented, command.ReceiptRequirementOverride);

        // The return half, priced by exactly the rule a standalone linked return uses.
        var pricedReturn = ReturnPricer.Price(
            sale, command.ReturnLines, _returnPolicy, command.NonReturnableOverride, _rounding,
            _settings.Policy.RestockingFeeRate);

        // The replacement half, priced against the live catalogue - see the class remarks.
        var warnings = new List<string>();
        var pricedReplacement = await PriceReplacementAsync(command.ReplacementLines, warnings, cancellationToken)
            .ConfigureAwait(false);

        var replacementPreDiscountTotal = pricedReplacement.Subtotal + pricedReplacement.Tax;
        var settlement = ExchangeSettlement.Calculate(pricedReturn.TotalRefund, replacementPreDiscountTotal);

        if (settlement.LeftoverRefund.IsPositive)
        {
            var isCashRefund = command.ExcessRefundMethod == RefundMethod.Cash;
            _returnPolicy.AuthoriseCashRefundLimit(isCashRefund, settlement.LeftoverRefund, command.CashRefundLimitOverride);
        }

        // Rounding point two, for the replacement sale - the credit is applied before rounding,
        // exactly where CompleteSaleHandler.PriceAsync applies a bill discount before its own
        // rounding call (CLAUDE.md invariant 2).
        var total = _rounding.Round(pricedReplacement.Subtotal - settlement.CreditApplied + pricedReplacement.Tax);
        var billRounding = Money.FromScaled(
            total.ToScaled() - pricedReplacement.Subtotal.ToScaled()
            + settlement.CreditApplied.ToScaled() - pricedReplacement.Tax.ToScaled());

        RequireReplacementLinesBalance(pricedReplacement, total, settlement.CreditApplied, billRounding);

        if (!total.IsZero && (command.DifferenceTenders is null || command.DifferenceTenders.Count == 0))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{total} is still owed on this exchange. Offer a tender for the difference."));
        }

        if (total.IsZero && command.DifferenceTenders is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "Nothing is owed on this exchange - the return covers it - so no tender should be offered.");
        }

        var tenderPlan = total.IsPositive
            ? TenderCalculator.Calculate(
                total,
                [.. command.DifferenceTenders.Select(t => new TenderLine(t.TenderType, t.Amount, t.Reference))])
            : new TenderPlan([], Money.Zero);

        var policyText = await ReturnPolicyTextBuilder.BuildAsync(_settings, _categories, cancellationToken)
            .ConfigureAwait(false);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var businessDate = DateOnly.FromDateTime(command.ExchangedAt.Date);

                // The sale is inserted first: sale_return.exchange_sale_id references sale(id),
                // so the row it points to has to exist before the return can be written.
                var billNo = await _numbers.AllocateAsync(SaleDocumentType, businessDate, token).ConfigureAwait(false);

                var saleId = await _sales.InsertSaleAsync(
                    new NewSale(
                        billNo,
                        command.ExchangedAt,
                        businessDate,
                        command.UserId,
                        command.ShiftId,
                        sale.CustomerId,
                        pricedReplacement.Subtotal,
                        Money.Zero,
                        settlement.CreditApplied,
                        pricedReplacement.Tax,
                        billRounding,
                        total,
                        pricedReplacement.Cogs),
                    token).ConfigureAwait(false);

                foreach (var line in pricedReplacement.Lines)
                {
                    await _sales.InsertSaleLineAsync(saleId, line.ToNewSaleLine(), token).ConfigureAwait(false);
                }

                foreach (var tender in tenderPlan.Applied)
                {
                    await _sales.InsertPaymentAsync(
                        saleId,
                        new NewTender(tender.TenderType, tender.Amount, tender.Reference, command.ExchangedAt),
                        token).ConfigureAwait(false);
                }

                foreach (var line in pricedReplacement.Lines)
                {
                    if (!line.PostsStock)
                    {
                        continue;
                    }

                    await _stock.PostAsync(
                        new StockPosting(
                            line.ProductVariantId!.Value,
                            SaleMovementType,
                            line.QuantityBase.Negate(),
                            line.UnitCost,
                            SaleMovementType,
                            saleId,
                            command.UserId,
                            command.ExchangedAt),
                        token).ConfigureAwait(false);

                    if (line.WentNegative)
                    {
                        await _audit.RecordAsync(
                            new AuditEntry(
                                command.ExchangedAt,
                                command.UserId,
                                NegativeStockAuditAction,
                                "product_variant",
                                line.ProductVariantId,
                                AfterJson: NegativeStockAuditPayload(billNo, line)),
                            token).ConfigureAwait(false);
                    }
                }

                var returnNo = await _numbers.AllocateAsync(ReturnDocumentType, sale.BusinessDate, token)
                    .ConfigureAwait(false);

                var saleReturnId = await _returns.InsertSaleReturnAsync(
                    new NewSaleReturn(
                        returnNo,
                        sale.SaleId,
                        command.ExchangedAt,
                        sale.BusinessDate,
                        sale.CustomerId,
                        command.UserId,
                        command.ShiftId,
                        pricedReturn.Subtotal,
                        pricedReturn.Tax,
                        pricedReturn.RestockingFee,
                        pricedReturn.TotalRefund,
                        RefundMethodMapping.ToAuditToken(RefundMethod.Exchange),
                        AuthorisedBy(command),
                        command.Reason,
                        ExchangeSaleId: saleId),
                    token).ConfigureAwait(false);

                foreach (var line in pricedReturn.Lines)
                {
                    await _returns.InsertSaleReturnLineAsync(saleReturnId, line.ToNewSaleReturnLine(), token)
                        .ConfigureAwait(false);

                    await _returns.IncrementQtyReturnedAsync(line.SaleLineId, line.QuantityBase, token)
                        .ConfigureAwait(false);

                    if (line.ProductVariantId is { } variantId && line.Disposition == ReturnDisposition.Sellable)
                    {
                        await _stock.PostAsync(
                            new StockPosting(
                                variantId,
                                ReturnInMovementType,
                                line.QuantityBase,
                                line.UnitCost,
                                ReturnDocumentType,
                                saleReturnId,
                                command.UserId,
                                command.ExchangedAt),
                            token).ConfigureAwait(false);
                    }
                }

                if (settlement.LeftoverRefund.IsPositive)
                {
                    await _returns.InsertRefundPaymentAsync(
                        saleReturnId,
                        new NewTender(
                            RefundMethodMapping.ToTenderType(command.ExcessRefundMethod),
                            settlement.LeftoverRefund.Negate(),
                            null,
                            command.ExchangedAt),
                        token).ConfigureAwait(false);
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.ExchangedAt,
                        command.UserId,
                        SaleAuditAction,
                        "sale",
                        saleId,
                        AfterJson: SaleAuditPayload(billNo, total, saleReturnId),
                        Reason: command.Reason),
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.ExchangedAt,
                        command.UserId,
                        ReturnAuditAction,
                        "sale_return",
                        saleReturnId,
                        AfterJson: ReturnAuditPayload(returnNo, pricedReturn.TotalRefund, saleId),
                        Reason: command.Reason),
                    token).ConfigureAwait(false);

                var payload = _receipts.Render(new ExchangeReceipt(
                    returnNo,
                    sale.BillNo,
                    billNo,
                    command.ExchangedAt,
                    [.. pricedReturn.Lines.Select(line => line.ToReceiptLine())],
                    pricedReturn.TotalRefund,
                    pricedReturn.RestockingFee,
                    [.. pricedReplacement.Lines.Select(line => line.ToReceiptLine())],
                    replacementPreDiscountTotal,
                    settlement.CreditApplied,
                    total,
                    tenderPlan.Applied.Select(t => new SaleReceiptTender(t.TenderType, t.Amount)).ToList(),
                    tenderPlan.Change,
                    settlement.LeftoverRefund,
                    settlement.LeftoverRefund.IsPositive ? RefundMethodMapping.ToAuditToken(command.ExcessRefundMethod) : null,
                    seller.DisplayName,
                    policyText));

                // One outbox row for both documents (CLAUDE.md invariant 7) - anchored on the sale,
                // the same doc_type/doc_id shape a plain sale receipt uses, so a future reprint path
                // finds it the same way ReprintReceiptHandler already looks a sale receipt up.
                var printJobId = await _printJobs
                    .EnqueueAsync(new PrintJobRequest(SaleDocumentType, saleId, payload), token)
                    .ConfigureAwait(false);

                return new CreatedExchange(
                    saleReturnId,
                    returnNo,
                    saleId,
                    billNo,
                    pricedReturn.TotalRefund,
                    replacementPreDiscountTotal,
                    settlement.CreditApplied,
                    Money.FromDecimal(tenderPlan.Applied.Sum(t => t.Amount.Amount)),
                    tenderPlan.Change,
                    settlement.LeftoverRefund,
                    printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prices every replacement line against the live catalogue - the same primitives
    /// <c>CompleteSaleHandler</c> uses, duplicated rather than shared (see the class remarks).
    /// </summary>
    private async Task<PricedReplacement> PriceReplacementAsync(
        IReadOnlyList<SaleLineRequest> requests, List<string> warnings, CancellationToken cancellationToken)
    {
        if (requests is null || requests.Count == 0)
        {
            throw new InvalidOperationException("An exchange must offer at least one replacement item.");
        }

        var lines = new List<PricedReplacementLine>(requests.Count);
        var subtotal = Money.Zero;
        var tax = Money.Zero;
        var cogs = Money.Zero;
        var lineNo = 1;

        // Two replacement lines can name the same variant within one exchange, exactly as two
        // lines of a bill can (CompleteSaleHandler.PriceAsync's own comment on this). Each line's
        // on-hand snapshot from the catalogue is the same pre-exchange balance - nothing has
        // posted yet - so a negative-stock check that only ever looks at one line at a time never
        // sees what earlier lines of the same variant, in this same exchange, have already
        // claimed against it. This accumulates that claim per variant as lines are priced, so the
        // second and later line of a repeated variant is checked (and, for Warn, reports its
        // message) against on-hand *net of what this exchange already committed to it* - not the
        // raw catalogue figure the first line saw. Mirrors P1-T09's fix in CompleteSaleHandler.
        var claimedByVariant = new Dictionary<long, Quantity>();

        foreach (var request in requests)
        {
            request.RequireWellFormed();

            var line = request.IsOpenItem
                ? PriceOpenItem(request, lineNo)
                : await PriceCatalogueLineAsync(request, lineNo, warnings, claimedByVariant, cancellationToken).ConfigureAwait(false);

            lineNo++;
            lines.Add(line);
            subtotal += line.LineTotal;
            tax += line.Tax;
            cogs += line.UnitCost * line.QuantityBase.Value;
        }

        return new PricedReplacement(lines, subtotal, tax, cogs, warnings);
    }

    private async Task<PricedReplacementLine> PriceCatalogueLineAsync(
        SaleLineRequest request,
        int lineNo,
        List<string> warnings,
        Dictionary<long, Quantity> claimedByVariant,
        CancellationToken cancellationToken)
    {
        var variantId = request.ProductVariantId!.Value;

        var item = await _catalogue.FindByVariantIdAsync(variantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Product variant {variantId} is not in the catalogue, or is no longer sellable."));

        var product = item.ToProduct();
        var uomId = request.UomId ?? item.BaseUomId;

        var quantityBase = UomConverter.ToBase(request.Quantity, uomId, product);
        var quantitySold = Quantity.FromDecimal(request.Quantity, uomId);
        var unitPrice = UomConverter.ResolvePrice(item.UnitPrice, uomId, product);

        var grossAmount = unitPrice * quantitySold.Value;
        var discountEvaluation = request.Discount is { } discount
            ? _discounts.AuthoriseLineDiscount(discount, grossAmount, item.MaxDiscountRate)
            : new DiscountEvaluation(Money.Zero, Percentage.Zero, item.MaxDiscountRate ?? _settings.Policy.MaxLineDiscountRate, ExceedsCap: false);

        var lineTotal = _rounding.Round(grossAmount - discountEvaluation.Amount);
        var lineTax = Money.FromScaled(item.TaxRate.TaxOnNet(lineTotal).ToScaled());

        var postsStock = !ProductTypes.PostsNoStockMovement(item.ProductType);

        // Net of whatever earlier replacement lines of this same variant, in this same exchange,
        // have already claimed against the catalogue's one pre-exchange balance (see the comment
        // on claimedByVariant in PriceReplacementAsync).
        var alreadyClaimed = claimedByVariant.TryGetValue(variantId, out var claimed)
            ? claimed
            : Quantity.Zero(item.QtyOnHand.UomId);
        var qtyOnHandForThisLine = item.QtyOnHand - alreadyClaimed;
        var wentNegative = postsStock && quantityBase.Value > qtyOnHandForThisLine.Value;

        if (postsStock)
        {
            claimedByVariant[variantId] = alreadyClaimed + quantityBase;
        }

        if (wentNegative)
        {
            RequireStockPolicy(item.Description, quantityBase, qtyOnHandForThisLine, item.UomSymbol, warnings);
        }

        return new PricedReplacementLine(
            lineNo,
            item.ProductVariantId,
            item.Description,
            item.UomSymbol,
            quantitySold,
            quantityBase,
            unitPrice,
            discountEvaluation.Amount,
            item.TaxRate,
            lineTax,
            lineTotal,
            item.UnitCost,
            postsStock,
            wentNegative);
    }

    private PricedReplacementLine PriceOpenItem(SaleLineRequest request, int lineNo)
    {
        var uomId = request.UomId!.Value;
        var unitPrice = request.OpenItemUnitPrice!.Value;
        var quantity = Quantity.FromDecimal(request.Quantity, uomId);

        var grossAmount = unitPrice * quantity.Value;
        var discountEvaluation = request.Discount is { } discount
            ? _discounts.AuthoriseLineDiscount(discount, grossAmount, productMaxDiscountRate: null)
            : new DiscountEvaluation(Money.Zero, Percentage.Zero, _settings.Policy.MaxLineDiscountRate, ExceedsCap: false);

        var lineTotal = _rounding.Round(grossAmount - discountEvaluation.Amount);
        var taxRate = _settings.Tax.DefaultTaxRate;
        var lineTax = Money.FromScaled(taxRate.TaxOnNet(lineTotal).ToScaled());

        return new PricedReplacementLine(
            lineNo,
            ProductVariantId: null,
            request.OpenItemDescription!,
            UomSymbolForOpenItem(uomId),
            quantity,
            quantity,
            unitPrice,
            discountEvaluation.Amount,
            taxRate,
            lineTax,
            lineTotal,
            Money.Zero,
            PostsStock: false,
            WentNegative: false);
    }

    private static string UomSymbolForOpenItem(long uomId) =>
        string.Create(CultureInfo.InvariantCulture, $"uom {uomId}");

    private void RequireStockPolicy(
        string description, Quantity quantityBase, Quantity qtyOnHand, string uomSymbol, List<string> warnings)
    {
        switch (_settings.Policy.NegativeStock)
        {
            case NegativeStockPolicy.Block:
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{description}' has only {qtyOnHand.Value} {uomSymbol} on hand. Giving out {quantityBase.Value} {uomSymbol} would take stock below zero, and the shop's policy blocks that."));

            case NegativeStockPolicy.Warn:
                warnings.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{description}' will go to {qtyOnHand.Value - quantityBase.Value} {uomSymbol} on hand - below zero."));
                break;

            case NegativeStockPolicy.Allow:
            default:
                break;
        }
    }

    private static void RequireReplacementLinesBalance(
        PricedReplacement replacement, Money total, Money creditApplied, Money rounding)
    {
        var subtotal = replacement.Subtotal.ToScaled();
        var lineTotals = replacement.Lines.Aggregate(0L, (running, line) => running + line.LineTotal.ToScaled());

        if (lineTotals != subtotal)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The replacement lines come to {Money.FromScaled(lineTotals)} but the subtotal is {Money.FromScaled(subtotal)}. They must match exactly before the exchange can be completed."));
        }

        var parts = subtotal - creditApplied.ToScaled() + replacement.Tax.ToScaled() + rounding.ToScaled();

        if (parts != total.ToScaled())
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The subtotal, credit, tax and rounding come to {Money.FromScaled(parts)} but the amount due is {total}. They must match exactly before the exchange can be completed."));
        }
    }

    private static void RequireAtLeastOneReturnLine(CreateExchangeCommand command)
    {
        if (command.ReturnLines is null || command.ReturnLines.Count == 0)
        {
            throw new InvalidOperationException("An exchange must return at least one line.");
        }
    }

    private static void RequireNoDuplicateReturnLines(CreateExchangeCommand command)
    {
        var seen = new HashSet<long>();
        foreach (var line in command.ReturnLines)
        {
            if (!seen.Add(line.SaleLineId))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bill line {line.SaleLineId} appears twice in the same exchange. Combine it into one line."));
            }
        }
    }

    private static void RequireCashOrCardOnly(RefundMethod excessRefundMethod)
    {
        RefundMethodMapping.RequireSupported(excessRefundMethod);

        if (excessRefundMethod != RefundMethod.Cash && excessRefundMethod != RefundMethod.Card)
        {
            throw new InvalidOperationException(
                "A lower-priced replacement's surplus is refunded by cash or card only.");
        }
    }

    private static void RequireSaleIsReturnable(ReturnableSale sale)
    {
        if (!string.Equals(sale.Status, CompletedSaleStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {sale.BillNo} is {sale.Status}, not completed, and has nothing left to exchange."));
        }
    }

    private AuthenticatedUser RequireTheSellerIsSignedIn(CreateExchangeCommand command)
    {
        var seller = _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. An exchange records who took it, so sign in before taking one.");

        if (seller.Id != command.UserId)
        {
            throw new InvalidOperationException(
                "This shift was opened by someone else. Close it and open a new one, so the exchange "
                + "records who actually took it.");
        }

        return seller;
    }

    private static long? AuthorisedBy(CreateExchangeCommand command) =>
        new[]
        {
            command.ReturnWindowOverride,
            command.NonReturnableOverride,
            command.ReceiptRequirementOverride,
            command.CashRefundLimitOverride,
        }
        .FirstOrDefault(token => token is { IsConsumed: true })
        ?.GrantedByUserId;

    private static string SaleAuditPayload(string billNo, Money total, long saleReturnId) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"bill_no":"{{billNo}}","total":{{total.ToScaled()}},"exchange_return_id":{{saleReturnId}}}""");

    private static string ReturnAuditPayload(string returnNo, Money totalRefund, long saleId) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"return_no":"{{returnNo}}","total_refund":{{totalRefund.ToScaled()}},"exchange_sale_id":{{saleId}}}""");

    private static string NegativeStockAuditPayload(string billNo, PricedReplacementLine line) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"bill_no":"{{billNo}}","variant_id":{{line.ProductVariantId}},"qty_sold_base":{{line.QuantityBase.ToScaled()}}}""");

    /// <summary>A replacement bill, priced and checked, ready to be written.</summary>
    private sealed record PricedReplacement(
        IReadOnlyList<PricedReplacementLine> Lines,
        Money Subtotal,
        Money Tax,
        Money Cogs,
        IReadOnlyList<string> Warnings);

    /// <summary>One priced replacement line - a catalogue line or an open item (SRS FR-2.8).</summary>
    private sealed record PricedReplacementLine(
        int LineNo,
        long? ProductVariantId,
        string Description,
        string UomSymbol,
        Quantity Quantity,
        Quantity QuantityBase,
        Money UnitPrice,
        Money Discount,
        TaxRate TaxRate,
        Money Tax,
        Money LineTotal,
        Money UnitCost,
        bool PostsStock,
        bool WentNegative)
    {
        internal NewSaleLine ToNewSaleLine() => new(
            LineNo, ProductVariantId, Description, Quantity, QuantityBase, UnitPrice, Discount, TaxRate, Tax, LineTotal, UnitCost);

        internal SaleReceiptLine ToReceiptLine() => new(Description, Quantity, UomSymbol, UnitPrice, LineTotal);
    }
}
