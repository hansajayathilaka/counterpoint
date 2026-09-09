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
/// Split tender, change and cash-drawer kick are P1-T10's; this still expects tenders to sum
/// exactly to the total. Trade price tiers and quantity breaks are Phase 5 (out of scope in
/// Phase 1) - <see cref="CompleteSaleCommand.CustomerId"/> attaches a customer for record-keeping
/// only and does not change what a line prices at.
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
        ISettings settings)
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
    }

    /// <inheritdoc />
    public async Task<CompletedSale> CompleteAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // First, before the catalogue is even read: a bill nobody can be held to is not a bill.
        RequireTheSellerIsSignedIn(command);

        // Priced before the transaction opens. Catalogue reads are not part of the write, and
        // the writer lock should be held for the writes and nothing else (NFR-P3).
        var bill = await PriceAsync(
            command.Lines,
            command.BillDiscount,
            DateOnly.FromDateTime(command.SoldAt.Date),
            cancellationToken).ConfigureAwait(false);

        RequireBillBalances(bill);
        RequireTendersMatch(command, bill.Total);

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

                foreach (var tender in command.Tenders)
                {
                    await _sales.InsertPaymentAsync(
                        saleId,
                        new NewTender(tender.TenderType, tender.Amount, tender.Reference, command.SoldAt),
                        token).ConfigureAwait(false);
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
                var payload = _receipts.Render(bill.ToReceipt(billNo, command));

                var printJobId = await _printJobs
                    .EnqueueAsync(new PrintJobRequest("SALE", saleId, payload), token)
                    .ConfigureAwait(false);

                return new CompletedSale(saleId, billNo, bill.Total, printJobId);
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

        return new SaleQuote(bill.ToQuotedLines(), bill.Subtotal, bill.BillDiscount, bill.Tax, bill.Total, bill.Warnings);
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
    /// Bill-level discount (SRS FR-3.17) is deliberately not spread across
    /// <c>sale_line.line_total</c>: it changes the header's <c>bill_discount</c> and
    /// <c>total</c> alone, so <c>sum(line_total) == subtotal</c> keeps holding over the lines as
    /// stored. <c>Domain.Services.DiscountAllocator</c> exists for the day a report or a return
    /// needs to know how much of a bill discount fell on one line; nothing in this phase needs it.
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

        var lines = new List<PricedLine>(requests.Count);
        var warnings = new List<string>();
        var subtotal = Money.Zero;
        var lineDiscount = Money.Zero;
        var tax = Money.Zero;
        var cogs = Money.Zero;
        var lineNo = 1;

        foreach (var request in requests)
        {
            request.RequireWellFormed();

            var line = request.IsOpenItem
                ? await PriceOpenItemAsync(request, lineNo, cancellationToken).ConfigureAwait(false)
                : await PriceCatalogueLineAsync(request, lineNo, warnings, cancellationToken).ConfigureAwait(false);

            lineNo++;
            lines.Add(line);

            subtotal += line.LineTotal;
            lineDiscount += line.Discount;
            tax += line.Tax;
            cogs += line.UnitCost * line.QuantityBase.Value;
        }

        var billDiscountEvaluation = billDiscount is { } requestedBillDiscount
            ? _discounts.AuthoriseBillDiscount(requestedBillDiscount, subtotal)
            : new DiscountEvaluation(Money.Zero, Percentage.Zero, _settings.Policy.MaxBillDiscountRate, ExceedsCap: false);

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

        // Rounding point one.
        var lineTotal = _rounding.Round(grossAmount - discountEvaluation.Amount);

        // Quantised to the storage scale here, once, so the value this line carries is the
        // value sale_line.tax will hold - and the bill's tax is the sum of exactly those.
        var lineTax = Money.FromScaled(item.TaxRate.TaxOnNet(lineTotal).ToScaled());

        var postsStock = !ProductTypes.PostsNoStockMovement(item.ProductType);
        var wentNegative = postsStock && quantityBase.Value > item.QtyOnHand.Value;

        if (wentNegative)
        {
            RequireStockPolicy(item.Description, quantityBase, item.QtyOnHand, item.UomSymbol, warnings);
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
            lineTax,
            lineTotal,
            item.UnitCost,
            postsStock,
            IsOpenItem: false,
            wentNegative);
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

        var lineTotal = _rounding.Round(grossAmount - discountEvaluation.Amount);
        var taxRate = _settings.Tax.DefaultTaxRate;
        var lineTax = Money.FromScaled(taxRate.TaxOnNet(lineTotal).ToScaled());

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
            lineTax,
            lineTotal,
            Money.Zero,
            PostsStock: false,
            IsOpenItem: true,
            WentNegative: false));
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
    /// Asserts the money adds up before anything is written. It refuses; it never corrects
    /// (engineering guide §4.1).
    /// </summary>
    private static void RequireTendersMatch(CompleteSaleCommand command, Money total)
    {
        if (command.Tenders is null || command.Tenders.Count == 0)
        {
            throw new InvalidOperationException("A completed bill must be tendered.");
        }

        var tendered = command.Tenders.Aggregate(Money.Zero, (running, tender) => running + tender.Amount);
        if (tendered != total)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The tenders come to {tendered} but the bill total is {total}. They must match exactly before the bill can be completed."));
        }
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
        internal SaleReceipt ToReceipt(string billNo, CompleteSaleCommand command) => new(
            billNo,
            command.SoldAt,
            [.. Lines.Select(line => new SaleReceiptLine(
                line.Description,
                line.Quantity,
                line.UomSymbol,
                line.UnitPrice,
                line.LineTotal))],
            Subtotal,
            Tax,
            Total,
            [.. command.Tenders.Select(tender => new SaleReceiptTender(tender.TenderType, tender.Amount))]);

        internal IReadOnlyList<QuotedLine> ToQuotedLines() =>
            [.. Lines.Select(line => new QuotedLine(
                line.ProductVariantId,
                line.Description,
                line.Quantity.Value,
                line.UomSymbol,
                line.UnitPrice,
                line.Discount,
                line.LineTotal,
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
        bool WentNegative)
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
