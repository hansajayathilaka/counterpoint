using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Pricing;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Sales;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// The sale commit, in exactly the shape SAD §7 specifies:
///
/// <code>
/// BEGIN IMMEDIATE
///   allocate bill_no from number_sequence
///   insert sale (+ prev_hash / row_hash)
///   insert sale_line[]
///   insert payment[]
///   post stock movements (+ balance projection)
///   insert audit_log
///   insert print_job                        -- outbox, not a printer call
/// COMMIT
/// </code>
///
/// If anything in that block throws, nothing happened except a consumed bill number - which is
/// correct and auditable (SRS FR-3.30).
/// </summary>
/// <remarks>
/// <para>
/// Prices in one call, sold in the next: unit conversion (SRS FR-2.4, FR-2.5, FR-3.6, FR-3.7),
/// open items (FR-2.8), line and bill discounts (FR-3.16, FR-3.17) and the negative-stock policy
/// (FR-3.13, FR-3.14, Q-11) all land here, in the Application layer, so the sales screen (P1-T09)
/// never computes a price or decides a policy - it only shows what this returns.
/// </para>
/// <para>
/// Split tender and change (P1-T10, SRS FR-3.24-FR-3.26) go through
/// <see cref="TenderCalculator"/>: every tender is applied to what the bill still owes, in the
/// order offered, and a cash tender may run over - the excess comes back as
/// <see cref="CompletedSale.Change"/>, never as a payment row. The cash-drawer kick itself is
/// embedded in the receipt byte stream by <c>Counterpoint.Devices.Printing.EscPosSaleReceiptRenderer</c>
/// (P0-T05) and dispatched by the print outbox, outside this transaction (CLAUDE.md invariant 7)
/// - there is no separate device call here. Trade price tiers and quantity breaks are Phase 5
/// (out of scope in Phase 1) - <see cref="CompleteSaleCommand.CustomerId"/> attaches a customer
/// for record-keeping only and does not change what a line prices at.
/// </para>
/// </remarks>
public sealed class CompleteSaleHandler : ICompleteSale, IQuoteSale
{
    /// <summary>The <c>number_sequence.doc_type</c> a bill is numbered from.</summary>
    private const string SaleDocumentType = "SALE";

    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> a bill posts.</summary>
    private const string SaleMovementType = "SALE";

    /// <summary>SRS FR-3.14 - "every negative-stock occurrence must be logged".</summary>
    private const string NegativeStockAuditAction = "NEGATIVE_STOCK_SALE";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IProductLookup _catalogue;
    private readonly ISaleWriter _sales;
    private readonly IStockLedger _stock;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly ISaleReceiptRenderer _receipts;
    private readonly IRoundingPolicy _rounding;
    private readonly ISession _session;
    private readonly IDiscountAuthorisationService _discounts;
    private readonly ISettings _settings;
    private readonly ICustomerStore _customers;
    private readonly ICreditNoteRedeemer _creditNotes;

    public CompleteSaleHandler(
        IUnitOfWork unitOfWork,
        IDocumentNumberAllocator numbers,
        IProductLookup catalogue,
        ISaleWriter sales,
        IStockLedger stock,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        ISaleReceiptRenderer receipts,
        IRoundingPolicy rounding,
        ISession session,
        IDiscountAuthorisationService discounts,
        ISettings settings,
        ICustomerStore customers,
        ICreditNoteRedeemer creditNotes)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(rounding);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(discounts);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(creditNotes);

        _unitOfWork = unitOfWork;
        _numbers = numbers;
        _catalogue = catalogue;
        _sales = sales;
        _stock = stock;
        _audit = audit;
        _printJobs = printJobs;
        _receipts = receipts;
        _rounding = rounding;
        _session = session;
        _discounts = discounts;
        _settings = settings;
        _customers = customers;
        _creditNotes = creditNotes;
    }

    /// <inheritdoc />
    public async Task<CompletedSale> CompleteAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // First, before the catalogue is even read: a bill nobody can be held to is not a bill.
        RequireTheSellerIsSignedIn(command);
        var seller = _session.CurrentUser!;

        // Priced before the transaction opens. Catalogue reads are not part of the write, and
        // the writer lock should be held for the writes and nothing else (NFR-P3).
        var bill = await PriceAsync(
            command.Lines,
            command.BillDiscount,
            DateOnly.FromDateTime(command.SoldAt.Date),
            cancellationToken).ConfigureAwait(false);

        RequireBillBalances(bill);

        // For the receipt only - never priced against, never a foreign key (CLAUDE.md invariant
        // 8's cousin: the till trades the same whether the name resolves or not). Read here, with
        // the pricing above, rather than inside the transaction (NFR-P3).
        var customer = command.CustomerId is { } customerId
            ? await _customers.FindByIdAsync(customerId, cancellationToken).ConfigureAwait(false)
            : null;

        // The authoritative split: every tender applied to what the bill still owes, in the
        // order offered, cash allowed to run over into change (SRS FR-3.24-FR-3.26). Computed
        // before the transaction opens, same as the pricing above - it refuses here or it does
        // not run at all.
        TenderTypes.RequireAccepted((command.Tenders ?? []).Select(tender => tender.TenderType));

        var tenderPlan = TenderCalculator.Calculate(
            bill.Total,
            [.. (command.Tenders ?? []).Select(tender => new TenderLine(tender.TenderType, tender.Amount, tender.Reference))]);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var billNo = await _numbers
                    .AllocateAsync(SaleDocumentType, bill.BusinessDate, token)
                    .ConfigureAwait(false);

                var saleId = await _sales.InsertSaleAsync(
                    new NewSale(
                        billNo,
                        command.SoldAt,
                        bill.BusinessDate,
                        command.UserId,
                        command.ShiftId,
                        command.CustomerId,
                        bill.Subtotal,
                        bill.LineDiscount,
                        bill.BillDiscount,
                        bill.Tax,
                        bill.Rounding,
                        bill.Total,
                        bill.Cogs),
                    token).ConfigureAwait(false);

                foreach (var line in bill.Lines)
                {
                    await _sales.InsertSaleLineAsync(saleId, line.ToNewSaleLine(), token)
                        .ConfigureAwait(false);
                }

                // Applied amounts, not what was offered: a payment row is what the bill was
                // actually paid, never what a cash tender ran over by (SRS FR-3.26). This is
                // what keeps sum(payment.amount) == sale.total exactly, for every split.
                foreach (var tender in tenderPlan.Applied)
                {
                    await _sales.InsertPaymentAsync(
                        saleId,
                        new NewTender(tender.TenderType, tender.Amount, tender.Reference, command.SoldAt),
                        token).ConfigureAwait(false);

                    // Store credit (SRS FR-5 store credit, FR-3 tender, task P2-T05): the credit
                    // note is spent here, inside this same sale transaction, with the guarded
                    // decrement ICreditNoteRedeemer.RedeemAsync performs - not before the
                    // transaction opens (task P2-T05's own over-redemption risk note). The
                    // convention: TenderRequest.Reference carries the credit note's own number,
                    // exactly as TenderTypes.CreditNote documents.
                    if (string.Equals(tender.TenderType, TenderTypes.CreditNote, StringComparison.Ordinal))
                    {
                        await _creditNotes.RedeemAsync(
                            RequireCreditNoteReference(tender.Reference),
                            tender.Amount,
                            saleId,
                            command.SoldAt,
                            bill.BusinessDate,
                            token).ConfigureAwait(false);
                    }
                }

                foreach (var line in bill.Lines)
                {
                    // Open items and SERVICE/NON_INVENTORY products post no stock movement at all
                    // (SRS FR-2.1-FR-2.8, FR-2.8); everything else goes through the one door stock
                    // is ever allowed through (CLAUDE.md invariant 3).
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
                            command.SoldAt),
                        token).ConfigureAwait(false);

                    if (line.WentNegative)
                    {
                        // SRS FR-3.14 - every negative-stock occurrence is logged, whether the
                        // shop's policy (Q-11) is "allow" or "warn"; only "block" ever reaches
                        // here having refused the line instead (RequireStockPolicy, below).
                        await _audit.RecordAsync(
                            new AuditEntry(
                                command.SoldAt,
                                command.UserId,
                                NegativeStockAuditAction,
                                "product_variant",
                                line.ProductVariantId,
                                AfterJson: NegativeStockAuditPayload(billNo, line)),
                            token).ConfigureAwait(false);
                    }
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.SoldAt,
                        command.UserId,
                        "SALE_COMPLETED",
                        "sale",
                        saleId,
                        AfterJson: AuditPayload(billNo, bill.Total)),
                    token).ConfigureAwait(false);

                // Rendered here, inside the transaction, because the bill number only exists
                // once number_sequence has been read and the outbox row must carry the finished
                // stream. This is a pure in-memory byte transform - no device, no I/O. The
                // printer itself is only ever touched by PrintWorker, outside any transaction
                // (CLAUDE.md invariant 7).
                var payload = _receipts.Render(bill.ToReceipt(
                    billNo,
                    command.SoldAt,
                    tenderPlan.Applied,
                    tenderPlan.Change,
                    seller.DisplayName,
                    customer,
                    _settings.Tax.TaxLabel));

                var printJobId = await _printJobs
                    .EnqueueAsync(
                        new PrintJobRequest("SALE", saleId, payload, Copies: _settings.Peripherals.ReceiptCopies),
                        token)
                    .ConfigureAwait(false);

                return new CompletedSale(saleId, billNo, bill.Total, tenderPlan.Change, printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SaleQuote> QuoteAsync(
        IReadOnlyList<SaleLineRequest> lines,
        DiscountInput? billDiscount = null,
        CancellationToken cancellationToken = default)
    {
        var bill = await PriceAsync(lines, billDiscount, DateOnly.MinValue, cancellationToken).ConfigureAwait(false);

        // The screen shows what the customer reads on the receipt: each line at what it is charged,
        // and a sub total of those - gross of tax in an inclusive shop (SaleReceiptFigures).
        var chargedSubtotal = bill.Lines.Aggregate(Money.Zero, (running, line) => running + line.Charged);

        return new SaleQuote(bill.ToQuotedLines(), chargedSubtotal, bill.BillDiscount, bill.Tax, bill.Total, bill.Warnings);
    }

    /// <summary>
    /// Prices the bill, before a single row is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two rounding points, and only those two (CLAUDE.md invariant 2): the line total, and
    /// the bill total. Everything between them is exact decimal arithmetic.
    /// </para>
    /// <para>
    /// Line tax is quantised to the <em>storage</em> scale as it is accumulated. That is not a
    /// third rounding-policy decision - <see cref="Money.ToScaled"/> applies exactly this
    /// quantisation on the way to disk anyway, for every line and for the header alike. Doing it
    /// here rather than letting it happen twice independently is what makes
    /// <c>sum(sale_line.tax) == sale.tax</c> hold over the rows as stored instead of only over
    /// the decimals in memory. The bill's rounding is then derived from the scaled quantities
    /// for the same reason, so the reconciliation identity is true by construction rather than
    /// by the line errors happening to cancel.
    /// </para>
    /// <para>
    /// <b>Tax follows the shop's pricing mode</b> (<c>tax.prices_include_tax</c>, SRS FR-10.3):
    /// added on top of the charged amount in an exclusive shop, carved out of it in an inclusive
    /// one - where the shelf price is what the customer pays. <c>sale_line.line_total</c> is the
    /// charged amount less the line's own tax in the inclusive case, so
    /// <c>subtotal - bill_discount + tax</c> is exactly what the customer pays in both modes.
    /// </para>
    /// <para>
    /// <b>The bill discount reduces the tax base</b> (SRS FR-3.17, §10.1: "Taxable value" is the
    /// sub total less the discount). It is split across the lines by
    /// <see cref="BillDiscountSplit"/> - with weights recomputable from the stored line, so a
    /// return or a report gets the same split back - and each line is taxed on its charged amount
    /// less its share. The discount itself stays on the header (<c>bill_discount</c>), so
    /// <c>sum(line_total) == subtotal</c> keeps holding over the lines as stored, and
    /// <c>subtotal - bill_discount</c> is the bill's revenue net of tax in either mode.
    /// </para>
    /// </remarks>
    private async Task<PricedBill> PriceAsync(
        IReadOnlyList<SaleLineRequest> requests,
        DiscountInput? billDiscount,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        if (requests is null || requests.Count == 0)
        {
            throw new InvalidOperationException("A bill must have at least one line.");
        }

        var drafts = new List<PricedLine>(requests.Count);
        var warnings = new List<string>();
        var lineNo = 1;

        // Two separate lines can name the same product variant within one bill (SRS FR-3.2's
        // CombineRepeatScans = false lets a repeat scan open a new line instead of folding into
        // the existing one). Each line's on-hand snapshot from the catalogue is the same
        // pre-sale balance - nothing has posted yet (stock posts after every line is priced) -
        // so a negative-stock check that only ever looks at one line at a time never sees what
        // earlier lines of the same variant, in this same bill, have already claimed against
        // it. This accumulates that claim per variant as lines are priced, so the second and
        // later line of a repeated variant is checked (and, for Warn, reports its message)
        // against on-hand *net of what this bill already committed to it* - not the raw
        // catalogue figure the first line saw.
        var claimedByVariant = new Dictionary<long, Quantity>();

        foreach (var request in requests)
        {
            request.RequireWellFormed();

            var line = request.IsOpenItem
                ? await PriceOpenItemAsync(request, lineNo, cancellationToken).ConfigureAwait(false)
                : await PriceCatalogueLineAsync(request, lineNo, warnings, claimedByVariant, cancellationToken).ConfigureAwait(false);

            lineNo++;
            drafts.Add(line);
        }

        // The bill discount is a discount off what the customer sees the lines come to - the
        // "Sub total" row of the receipt - in either pricing mode.
        var chargedSubtotal = drafts.Aggregate(Money.Zero, (running, line) => running + line.Charged);

        var billDiscountEvaluation = billDiscount is { } requestedBillDiscount
            ? _discounts.AuthoriseBillDiscount(requestedBillDiscount, chargedSubtotal)
            : new DiscountEvaluation(Money.Zero, Percentage.Zero, _settings.Policy.MaxBillDiscountRate, ExceedsCap: false);

        var shares = BillDiscountSplit.Allocate(billDiscountEvaluation.Amount, [.. drafts.Select(line => line.Weight)]);
        var pricesIncludeTax = _settings.Tax.PricesIncludeTax;

        var lines = new List<PricedLine>(drafts.Count);
        var subtotal = Money.Zero;
        var lineDiscount = Money.Zero;
        var tax = Money.Zero;
        var cogs = Money.Zero;

        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            // Never below zero: the split is weighted by the unrounded line value, so on a bill
            // discounted to nothing one line's share can sit a fraction above its rounded charge.
            var taxBase = draft.Charged - shares[i];
            var lineTax = LineTaxCalculator.TaxOnCharged(
                taxBase.IsNegative ? Money.Zero : taxBase, draft.TaxRate, pricesIncludeTax);

            // Exact scaled subtraction in the inclusive case (LineTaxCalculator's own reasoning):
            // line_total + tax reconstructs the charged amount bit for bit.
            var lineTotal = pricesIncludeTax
                ? Money.FromScaled(draft.Charged.ToScaled() - lineTax.ToScaled())
                : draft.Charged;

            var line = draft with { Tax = lineTax, LineTotal = lineTotal, BillDiscountShare = shares[i] };
            lines.Add(line);

            subtotal += line.LineTotal;
            lineDiscount += line.Discount;
            tax += line.Tax;
            cogs += line.UnitCost * line.QuantityBase.Value;
        }

        // Rounding point two. The identity below is the real one, not a special case of it, even
        // when there is no bill discount at all (billDiscountEvaluation.Amount is then zero).
        var total = _rounding.Round(subtotal - billDiscountEvaluation.Amount + tax);

        // Derived from the scaled quantities, not from a decimal subtraction: rounding is the
        // column that has to make subtotal - bill_discount + tax + rounding = total add up in
        // the row as stored, so it is computed in the arithmetic the row is stored in.
        var rounding = Money.FromScaled(
            total.ToScaled() - subtotal.ToScaled() + billDiscountEvaluation.Amount.ToScaled() - tax.ToScaled());

        return new PricedBill(
            businessDate,
            lines,
            subtotal,
            lineDiscount,
            billDiscountEvaluation.Amount,
            tax,
            rounding,
            total,
            cogs,
            warnings);
    }

    /// <summary>
    /// Prices one catalogue line: resolves the selling unit's price (SRS FR-2.5, FR-3.7),
    /// converts to base units and validates the product's quantity rules (FR-2.1-FR-2.8) through
    /// <see cref="UomConverter"/>, applies the line discount if one was asked for (FR-3.16), and
    /// checks the negative-stock policy (FR-3.13, FR-3.14, Q-11).
    /// </summary>
    private async Task<PricedLine> PriceCatalogueLineAsync(
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

        // Validates the product's own quantity rule (STANDARD: whole units; DECIMAL: at most the
        // unit's own decimal places) and converts to the base unit stock is always held in.
        var quantityBase = UomConverter.ToBase(request.Quantity, uomId, product);
        var quantitySold = Quantity.FromDecimal(request.Quantity, uomId);
        var unitPrice = UomConverter.ResolvePrice(item.UnitPrice, uomId, product);

        var grossAmount = unitPrice * quantitySold.Value;
        var discountEvaluation = request.Discount is { } discount
            ? _discounts.AuthoriseLineDiscount(discount, grossAmount, item.MaxDiscountRate)
            : new DiscountEvaluation(Money.Zero, Percentage.Zero, item.MaxDiscountRate ?? _settings.Policy.MaxLineDiscountRate, ExceedsCap: false);

        // Rounding point one. Tax is taken from this in PriceAsync, once the line's share of any
        // bill discount is known.
        var charged = _rounding.Round(grossAmount - discountEvaluation.Amount);

        var postsStock = !ProductTypes.PostsNoStockMovement(item.ProductType);

        // Net of whatever earlier lines of this same variant, in this same bill, have already
        // claimed against the catalogue's one pre-sale balance (see the comment on
        // claimedByVariant in PriceAsync). The first line of a variant sees the raw balance,
        // exactly as before; a repeated line sees it reduced by its own sibling lines.
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

        return new PricedLine(
            lineNo,
            item.ProductVariantId,
            item.Description,
            item.UomSymbol,
            quantitySold,
            quantityBase,
            unitPrice,
            discountEvaluation.Amount,
            item.TaxRate,
            Money.Zero,
            charged,
            item.UnitCost,
            postsStock,
            IsOpenItem: false,
            wentNegative,
            charged,
            BillDiscountSplit.Weight(unitPrice, quantitySold, discountEvaluation.Amount),
            Money.Zero);
    }

    /// <summary>
    /// Prices one open item line (SRS FR-2.8): a manually typed description and price, taxed at
    /// the shop's default rate because there is no product tax class to read one from, and never
    /// posting a stock movement because there is no variant to post one against.
    /// </summary>
    private Task<PricedLine> PriceOpenItemAsync(SaleLineRequest request, int lineNo, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var uomId = request.UomId!.Value;
        var unitPrice = request.OpenItemUnitPrice!.Value;
        var quantity = Quantity.FromDecimal(request.Quantity, uomId);

        var grossAmount = unitPrice * quantity.Value;
        var discountEvaluation = request.Discount is { } discount
            ? _discounts.AuthoriseLineDiscount(discount, grossAmount, productMaxDiscountRate: null)
            : new DiscountEvaluation(Money.Zero, Percentage.Zero, _settings.Policy.MaxLineDiscountRate, ExceedsCap: false);

        var charged = _rounding.Round(grossAmount - discountEvaluation.Amount);
        var taxRate = _settings.Tax.DefaultTaxRate;

        return Task.FromResult(new PricedLine(
            lineNo,
            ProductVariantId: null,
            request.OpenItemDescription!,
            UomSymbolForOpenItem(uomId),
            quantity,
            quantity,
            unitPrice,
            discountEvaluation.Amount,
            taxRate,
            Money.Zero,
            charged,
            Money.Zero,
            PostsStock: false,
            IsOpenItem: true,
            WentNegative: false,
            charged,
            BillDiscountSplit.Weight(unitPrice, quantity, discountEvaluation.Amount),
            Money.Zero));
    }

    /// <summary>
    /// An open item has no product to read a unit symbol from - the receipt and the screen still
    /// need something to print beside the quantity, so this names the unit by its id until the
    /// caller resolves it. The sales screen (P1-T09) always has the symbol on hand from
    /// <c>IUomStore</c> already and overwrites this on the DTO it builds for display; what
    /// reaches <c>sale_line</c> is <c>uom_id</c> itself, not this text.
    /// </summary>
    private static string UomSymbolForOpenItem(long uomId) =>
        string.Create(CultureInfo.InvariantCulture, $"uom {uomId}");

    /// <summary>
    /// Applies the shop's negative-stock policy (SRS FR-3.13, Q-11) to one line that would take
    /// the balance below zero.
    /// </summary>
    /// <exception cref="InvalidOperationException">The policy is <see cref="NegativeStockPolicy.Block"/>.</exception>
    private void RequireStockPolicy(
        string description,
        Quantity quantityBase,
        Quantity qtyOnHand,
        string uomSymbol,
        List<string> warnings)
    {
        switch (_settings.Policy.NegativeStock)
        {
            case NegativeStockPolicy.Block:
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{description}' has only {qtyOnHand.Value} {uomSymbol} on hand. Selling {quantityBase.Value} {uomSymbol} would take stock below zero, and the shop's policy blocks that."));

            case NegativeStockPolicy.Warn:
                warnings.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{description}' will go to {qtyOnHand.Value - quantityBase.Value} {uomSymbol} on hand - below zero."));
                break;

            case NegativeStockPolicy.Allow:
            default:
                // Sold anyway, logged at completion regardless (FR-3.14) - nothing to say here.
                break;
        }
    }

    /// <summary>
    /// Asserts the two bill identities of engineering guide §4.1 before anything is written:
    /// <c>sum(line_total) == subtotal</c> and
    /// <c>subtotal - bill_discount + tax + rounding == total</c>. It refuses; it never corrects.
    /// </summary>
    /// <remarks>
    /// Compared as scaled integers, over the values <em>as they will be stored</em>. A decimal
    /// comparison can pass on a bill whose stored row is out by one scaled unit, and it is the
    /// stored row that a Z report reconciles and a hash chain seals - so the stored row is what
    /// gets checked.
    /// </remarks>
    private static void RequireBillBalances(PricedBill bill)
    {
        var subtotal = bill.Subtotal.ToScaled();
        var lineTotals = bill.Lines.Aggregate(0L, (running, line) => running + line.LineTotal.ToScaled());

        if (lineTotals != subtotal)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The bill lines come to {Money.FromScaled(lineTotals)} but the subtotal is {Money.FromScaled(subtotal)}. They must match exactly before the bill can be completed."));
        }

        var parts = subtotal - bill.BillDiscount.ToScaled() + bill.Tax.ToScaled() + bill.Rounding.ToScaled();
        var total = bill.Total.ToScaled();

        if (parts != total)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The subtotal, discount, tax and rounding come to {Money.FromScaled(parts)} but the bill total is {Money.FromScaled(total)}. They must match exactly before the bill can be completed."));
        }
    }

    /// <summary>
    /// Asserts that the person completing the bill is the person the bill will be stamped with
    /// (SRS FR-1.1, FR-1.6).
    /// </summary>
    private void RequireTheSellerIsSignedIn(CompleteSaleCommand command)
    {
        var seller = _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A bill records who sold it, so sign in before completing one.");

        if (seller.Id != command.UserId)
        {
            throw new InvalidOperationException(
                "This shift was opened by someone else. Close it and open a new one, so the bill "
                + "records who actually sold it.");
        }
    }

    /// <summary>
    /// The convention a <c>CREDIT_NOTE</c> tender uses to name which note it is spending
    /// (task P2-T05): <see cref="TenderRequest.Reference"/> carries the credit note's own
    /// <c>number</c>, and nothing else does. A tender of this type with no reference is a
    /// malformed request, not a note this system could ever look up.
    /// </summary>
    private static string RequireCreditNoteReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException(
                "A CREDIT_NOTE tender must carry the credit note's own number in its reference.");
        }

        return reference;
    }

    /// <summary>
    /// The audit row's after-state. Written by hand rather than serialised so the text is
    /// stable byte for byte - it is about to be hashed into a chain.
    /// </summary>
    private static string AuditPayload(string billNo, Money total) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"bill_no":"{{billNo}}","total":{{total.ToScaled()}}}""");

    /// <summary>The negative-stock audit row's after-state (SRS FR-3.14).</summary>
    private static string NegativeStockAuditPayload(string billNo, PricedLine line) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"bill_no":"{{billNo}}","variant_id":{{line.ProductVariantId}},"qty_sold_base":{{line.QuantityBase.ToScaled()}}}""");

    /// <summary>A bill, priced and checked, ready to be written.</summary>
    private sealed record PricedBill(
        DateOnly BusinessDate,
        IReadOnlyList<PricedLine> Lines,
        Money Subtotal,
        Money LineDiscount,
        Money BillDiscount,
        Money Tax,
        Money Rounding,
        Money Total,
        Money Cogs,
        IReadOnlyList<string> Warnings)
    {
        internal SaleReceipt ToReceipt(
            string billNo,
            DateTimeOffset soldAt,
            IReadOnlyList<AppliedTender> tenders,
            Money change,
            string cashierName,
            CustomerRecord? customer,
            string taxLabel)
        {
            // One derivation for the original and every reprint (SaleReceiptFigures) - open items
            // are taxed at the shop's default rate and fall into that same tax row when it matches.
            return SaleReceiptFigures.Build(
                billNo,
                soldAt,
                [.. Lines.Select(line => new SaleReceiptLineFigures(
                    line.Description,
                    line.Quantity,
                    line.UomSymbol,
                    line.UnitPrice,
                    line.Charged,
                    line.LineTotal,
                    line.Tax,
                    line.TaxRate,
                    line.BillDiscountShare))],
                Subtotal,
                BillDiscount,
                Tax,
                Total,
                [.. tenders.Select(tender => new SaleReceiptTender(tender.TenderType, tender.Amount))],
                change,
                taxLabel,
                cashierName,
                customer?.Name ?? "Walk-in",
                string.Equals(customer?.Type, CustomerPriceTiers.TradeToken, StringComparison.Ordinal));
        }

        internal IReadOnlyList<QuotedLine> ToQuotedLines() =>
            [.. Lines.Select(line => new QuotedLine(
                line.ProductVariantId,
                line.Description,
                line.Quantity.Value,
                line.UomSymbol,
                line.UnitPrice,
                line.Discount,
                line.Charged,
                line.IsOpenItem))];
    }

    /// <summary>One priced bill line - a catalogue line or an open item (SRS FR-2.8).</summary>
    private sealed record PricedLine(
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
        bool IsOpenItem,
        bool WentNegative,
        Money Charged,
        Money Weight,
        Money BillDiscountShare)
    {
        internal NewSaleLine ToNewSaleLine() => new(
            LineNo,
            ProductVariantId,
            Description,
            Quantity,
            QuantityBase,
            UnitPrice,
            Discount,
            TaxRate,
            Tax,
            LineTotal,
            UnitCost);
    }
}
