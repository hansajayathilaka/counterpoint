using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
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
/// This is the walking skeleton's version. It sells whole base units at the catalogue price
/// with no discount and no tender beyond the exact amount. Discounts (P1-T08), unit conversion
/// (P1-T05), split tender and change (P1-T10) and negative-stock policy (P1-T09) all land in
/// the same shape, because the shape is what this task exists to establish.
/// </para>
/// </remarks>
public sealed class CompleteSaleHandler : ICompleteSale, IQuoteSale
{
    /// <summary>The <c>number_sequence.doc_type</c> a bill is numbered from.</summary>
    private const string SaleDocumentType = "SALE";

    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> a bill posts.</summary>
    private const string SaleMovementType = "SALE";

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
        ISession session)
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
                        bill.Subtotal,
                        Money.Zero,
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
                    // Every stock change goes through the ledger, which appends the movement and
                    // advances the projection in this same transaction (CLAUDE.md invariant 3).
                    await _stock.PostAsync(
                        new StockPosting(
                            line.ProductVariantId,
                            SaleMovementType,
                            line.QuantityBase.Negate(),
                            line.UnitCost,
                            SaleMovementType,
                            saleId,
                            command.UserId,
                            command.SoldAt),
                        token).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        var bill = await PriceAsync(lines, DateOnly.MinValue, cancellationToken).ConfigureAwait(false);

        return new SaleQuote(bill.ToQuotedLines(), bill.Subtotal, bill.Tax, bill.Total);
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
    /// </remarks>
    private async Task<PricedBill> PriceAsync(
        IReadOnlyList<SaleLineRequest> requests,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        if (requests is null || requests.Count == 0)
        {
            throw new InvalidOperationException("A bill must have at least one line.");
        }

        var lines = new List<PricedLine>(requests.Count);
        var subtotal = Money.Zero;
        var tax = Money.Zero;
        var cogs = Money.Zero;
        var lineNo = 1;

        foreach (var request in requests)
        {
            if (request.Quantity <= 0m)
            {
                throw new InvalidOperationException(
                    "A bill line must have a positive quantity. Removing an item is not a negative line.");
            }

            var item = await _catalogue.FindByVariantIdAsync(request.ProductVariantId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Product variant {request.ProductVariantId} is not in the catalogue, or is no longer sellable."));

            var quantity = Quantity.FromDecimal(request.Quantity, item.BaseUomId);

            // Rounding point one.
            var lineTotal = _rounding.Round(item.UnitPrice * quantity.Value);

            // Quantised to the storage scale here, once, so the value this line carries is the
            // value sale_line.tax will hold - and the bill's tax is the sum of exactly those.
            var lineTax = Money.FromScaled(item.TaxRate.TaxOnNet(lineTotal).ToScaled());

            lines.Add(new PricedLine(
                lineNo++,
                item,
                quantity,
                lineTotal,
                lineTax));

            subtotal += lineTotal;
            tax += lineTax;
            cogs += item.UnitCost * quantity.Value;
        }

        // Rounding point two. What it moved is recorded rather than absorbed (SRS FR-3.20).
        // The skeleton has no whole-bill discount yet (P1-T08); it is named rather than
        // inlined so the identity below is the real one and not a special case of it.
        var billDiscount = Money.Zero;
        var total = _rounding.Round(subtotal - billDiscount + tax);

        // Derived from the scaled quantities, not from a decimal subtraction: rounding is the
        // column that has to make subtotal - bill_discount + tax + rounding = total add up in
        // the row as stored, so it is computed in the arithmetic the row is stored in.
        var rounding = Money.FromScaled(
            total.ToScaled() - subtotal.ToScaled() + billDiscount.ToScaled() - tax.ToScaled());

        return new PricedBill(businessDate, lines, subtotal, billDiscount, tax, rounding, total, cogs);
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
    /// <remarks>
    /// <para>
    /// <c>sale.user_id</c> goes into an append-only, hash-chained row (CLAUDE.md invariants 5
    /// and 6). Once it is committed there is no correcting it, and every cashier report and
    /// every audit trail afterwards reads it as the truth about who sold what. So the one thing
    /// that must never happen is a bill committed under somebody else's name.
    /// </para>
    /// <para>
    /// It can happen today because the command's user id comes from the open shift - from
    /// whoever opened it - rather than from the session. One person per shift made that
    /// accidentally correct while there was one account in the whole shop; a second person
    /// signing in and trading would have their bills filed under the shift opener.
    /// </para>
    /// <para>
    /// <b>This is the guard, not the cure.</b> Stamping the bill with the cashier who is
    /// actually signed in - and letting a till change hands without closing the shift - is the
    /// sale path's own work in P1-T09 and P1-T10. Until then the shop trades one person to a
    /// shift, and this makes that a rule the Application layer enforces rather than an
    /// assumption the UI happens to satisfy.
    /// </para>
    /// </remarks>
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

    /// <summary>A bill, priced and checked, ready to be written.</summary>
    private sealed record PricedBill(
        DateOnly BusinessDate,
        IReadOnlyList<PricedLine> Lines,
        Money Subtotal,
        Money BillDiscount,
        Money Tax,
        Money Rounding,
        Money Total,
        Money Cogs)
    {
        internal SaleReceipt ToReceipt(string billNo, CompleteSaleCommand command) => new(
            billNo,
            command.SoldAt,
            [.. Lines.Select(line => new SaleReceiptLine(
                line.Item.Description,
                line.Quantity,
                line.Item.UomSymbol,
                line.Item.UnitPrice,
                line.LineTotal))],
            Subtotal,
            Tax,
            Total,
            [.. command.Tenders.Select(tender => new SaleReceiptTender(tender.TenderType, tender.Amount))]);

        internal IReadOnlyList<QuotedLine> ToQuotedLines() =>
            [.. Lines.Select(line => new QuotedLine(
                line.ProductVariantId,
                line.Item.Description,
                line.Quantity.Value,
                line.Item.UomSymbol,
                line.Item.UnitPrice,
                line.LineTotal))];
    }

    /// <summary>One priced bill line.</summary>
    private sealed record PricedLine(
        int LineNo,
        CatalogueItem Item,
        Quantity Quantity,
        Money LineTotal,
        Money Tax)
    {
        internal long ProductVariantId => Item.ProductVariantId;

        /// <summary>
        /// The skeleton sells in the product's base unit, so the sold quantity and the base
        /// quantity are the same value. Unit conversion (SRS FR-2.4, FR-3.7) is P1-T05's, and
        /// it changes this line and nothing else in the transaction.
        /// </summary>
        internal Quantity QuantityBase => Quantity;

        internal Money UnitCost => Item.UnitCost;

        internal NewSaleLine ToNewSaleLine() => new(
            LineNo,
            Item.ProductVariantId,
            Item.Description,
            Quantity,
            QuantityBase,
            Item.UnitPrice,
            Money.Zero,
            Item.TaxRate,
            Tax,
            LineTotal,
            Item.UnitCost);
    }
}
