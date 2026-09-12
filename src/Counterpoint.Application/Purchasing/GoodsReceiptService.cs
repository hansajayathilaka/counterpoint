using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Labels;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Purchasing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Purchasing;

/// <summary>
/// The owner's goods-receipt service (SRS FR-4.7, FR-4.8, AC-08) - the main inbound stock path.
/// </summary>
/// <remarks>
/// <para>
/// Internal, for the same reason every other owner-only Application service is
/// (<see cref="PurchaseOrderService"/>'s own remarks): the role check on
/// <see cref="IGoodsReceiptService"/> only holds if nothing outside this assembly can construct
/// the class the check is supposed to be in front of.
/// </para>
/// <para>
/// <b>The AC-08 risk, and how this closes it off.</b> Every line's quantity and cost are
/// converted to the product's base unit exactly once, in <see cref="PriceLineAsync"/>, before the
/// transaction opens - <see cref="Domain.Catalogue.UomConverter.ToBase"/> is the only place the
/// conversion factor is ever applied, and everything downstream (the freight apportionment, the
/// stock posting, the <c>product_supplier.last_cost</c> write, the price-review comparison)
/// consumes the already-converted <see cref="Quantity"/> and <see cref="Money"/> values. There is
/// no second place a pre-conversion quantity could leak into the moving-average recompute.
/// </para>
/// </remarks>
internal sealed class GoodsReceiptService : IGoodsReceiptService
{
    /// <summary>The <c>number_sequence.doc_type</c> a goods receipt is numbered from.</summary>
    private const string GoodsReceiptDocumentType = "GRN";

    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> a goods receipt posts.</summary>
    private const string GoodsReceiptMovementType = "GRN";

    private readonly IGoodsReceiptStore _store;
    private readonly ISupplierStore _suppliers;
    private readonly IPurchaseOrderStore _purchaseOrders;
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly IProductLookup _catalogue;
    private readonly IProductStore _products;
    private readonly IProductSupplierStore _productSuppliers;
    private readonly IStockLedger _stock;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly IGoodsReceiptDocumentRenderer _renderer;
    private readonly ILabelPrintService _labels;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public GoodsReceiptService(
        IGoodsReceiptStore store,
        ISupplierStore suppliers,
        IPurchaseOrderStore purchaseOrders,
        IPurchaseOrderService purchaseOrderService,
        IProductLookup catalogue,
        IProductStore products,
        IProductSupplierStore productSuppliers,
        IStockLedger stock,
        IDocumentNumberAllocator numbers,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        IGoodsReceiptDocumentRenderer renderer,
        ILabelPrintService labels,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(purchaseOrders);
        ArgumentNullException.ThrowIfNull(purchaseOrderService);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(productSuppliers);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _suppliers = suppliers;
        _purchaseOrders = purchaseOrders;
        _purchaseOrderService = purchaseOrderService;
        _catalogue = catalogue;
        _products = products;
        _productSuppliers = productSuppliers;
        _stock = stock;
        _numbers = numbers;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _printJobs = printJobs;
        _renderer = renderer;
        _labels = labels;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GoodsReceiptSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<GoodsReceiptRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _store.FindByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<GoodsReceiptResult> ReceiveAsync(
        CreateGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireSignedIn();

        var supplier = await _suppliers.FindByIdAsync(command.SupplierId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"There is no supplier with id {command.SupplierId}."));

        PurchaseOrderRecord? purchaseOrder = null;
        if (command.PurchaseOrderId is { } purchaseOrderId)
        {
            purchaseOrder = await _purchaseOrders.FindByIdAsync(purchaseOrderId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"There is no purchase order with id {purchaseOrderId}."));

            if (PurchaseOrderStatuses.Parse(purchaseOrder.Status) == PurchaseOrderStatus.Cancelled)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Purchase order {purchaseOrder.PoNo} is cancelled and cannot be received against."));
            }
        }

        if (command.OtherCost.IsNegative)
        {
            throw new InvalidOperationException("Freight/other cost cannot be negative.");
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new InvalidOperationException("A goods receipt needs at least one line.");
        }

        // Priced before the transaction opens - the same split CompleteSaleHandler and
        // PurchaseOrderService draw: catalogue reads and UOM conversion are not part of the
        // write, and the writer lock should be held for the writes and nothing else (NFR-P3).
        var lines = new List<PricedGoodsReceiptLine>(command.Lines.Count);
        foreach (var line in command.Lines)
        {
            lines.Add(await PriceLineAsync(line, cancellationToken).ConfigureAwait(false));
        }

        // The freight apportionment (P2-T07 "Do this" #3): each line's share of OtherCost,
        // proportional to its own pre-freight, pre-tax subtotal, exact to the scaled unit
        // (Counterpoint.Domain.Purchasing.GoodsReceiptFreightApportioner).
        var freightShares = GoodsReceiptFreightApportioner.Apportion(
            command.OtherCost,
            [.. lines.Select(line => line.Subtotal)]);

        var finished = new List<FinishedGoodsReceiptLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            finished.Add(lines[i].Finish(freightShares[i]));
        }

        var receivedAt = command.ReceivedAt ?? _timeProvider.GetLocalNow();
        var businessDate = DateOnly.FromDateTime(receivedAt.Date);

        var subtotal = Money.FromScaled(finished.Sum(line => line.Subtotal.ToScaled()));
        var tax = Money.FromScaled(finished.Sum(line => line.Tax.ToScaled()));
        var total = subtotal + tax + command.OtherCost;

        RequireReceiptBalances(subtotal, tax, command.OtherCost, total, finished);

        var priceReviewFlags = BuildPriceReviewFlags(finished);

        var receipt = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var grnNo = await _numbers.AllocateAsync(GoodsReceiptDocumentType, businessDate, token)
                    .ConfigureAwait(false);

                var receiptId = await _store.CreateAsync(
                    new NewGoodsReceipt(
                        grnNo,
                        command.SupplierId,
                        command.PurchaseOrderId,
                        Trimmed(command.SupplierInvoiceNo),
                        receivedAt,
                        subtotal,
                        tax,
                        command.OtherCost,
                        total,
                        actor.Id,
                        Trimmed(command.Note),
                        [.. finished.Select(line => line.ToNewLine())]),
                    token).ConfigureAwait(false);

                foreach (var line in finished)
                {
                    // The one door stock is ever allowed through (CLAUDE.md invariant 3). The
                    // movement's own cost is the landed cost per base unit - supplier price plus
                    // this line's freight share, converted before this call, never after - which
                    // is what StockLedgerMath.Apply recomputes the moving average against.
                    await _stock.PostAsync(
                        new StockPosting(
                            line.ProductVariantId,
                            GoodsReceiptMovementType,
                            line.QtyBase,
                            line.UnitCostBase,
                            GoodsReceiptMovementType,
                            receiptId,
                            actor.Id,
                            receivedAt),
                        token).ConfigureAwait(false);

                    // The supplier's own last-quoted price for this product - deliberately the
                    // raw supplier cost, not this shipment's landed cost (IProductSupplierStore's
                    // own remarks explain why).
                    await _productSuppliers.UpsertLastCostAsync(
                        line.ProductId,
                        command.SupplierId,
                        line.UnitCostBaseExcludingFreight,
                        token).ConfigureAwait(false);

                    if (command.PurchaseOrderId is { } linkedPurchaseOrderId)
                    {
                        await _purchaseOrders.IncrementReceivedAsync(
                            linkedPurchaseOrderId,
                            line.ProductVariantId,
                            line.QtyBase,
                            token).ConfigureAwait(false);
                    }
                }

                if (command.PurchaseOrderId is { } orderToRecompute)
                {
                    // The hook IPurchaseOrderService.RecomputeStatusAsync exists for (P2-T06's own
                    // remark on that method): re-entrant into the same ambient transaction, so
                    // this and everything above commit or roll back together.
                    await _purchaseOrderService.RecomputeStatusAsync(orderToRecompute, token).ConfigureAwait(false);
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        receivedAt,
                        actor.Id,
                        GoodsReceiptAuditActions.Created,
                        GoodsReceiptAuditActions.EntityType,
                        receiptId,
                        AfterJson: AuditPayload(grnNo, supplier.Name, total)),
                    token).ConfigureAwait(false);

                // A pure in-memory byte transform, rendered here because the GRN number only
                // exists once number_sequence has been read - the same reasoning
                // CompleteSaleHandler's own receipt render carries. No device, no I/O; the
                // printer itself is only ever touched by PrintWorker, outside any transaction
                // (CLAUDE.md invariant 7).
                var payload = _renderer.RenderPdf(new GoodsReceiptDocument(
                    grnNo,
                    receivedAt,
                    supplier.Name,
                    Trimmed(command.SupplierInvoiceNo),
                    purchaseOrder?.PoNo,
                    actor.DisplayName,
                    Trimmed(command.Note),
                    subtotal,
                    tax,
                    command.OtherCost,
                    total,
                    [.. finished.Select(line => line.ToDocumentLine())]));

                await _printJobs.EnqueueAsync(new PrintJobRequest("GRN", receiptId, payload), token)
                    .ConfigureAwait(false);

                return await _store.FindByIdAsync(receiptId, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (receipt is null)
        {
            throw new InvalidOperationException("The goods receipt was created but could not be read back.");
        }

        // Never inside the transaction just committed above (CLAUDE.md invariant 7): a real
        // label printer can take seconds to answer, and by this point the receipt has already
        // posted regardless of how printing goes (ILabelPrinter's own contract - a printer fault
        // comes back as a failed PrintOutcome, never an exception).
        var labelOutcome = await PrintLabelsAsync(finished, cancellationToken).ConfigureAwait(false);

        return new GoodsReceiptResult(receipt, priceReviewFlags, labelOutcome);
    }

    /// <summary>
    /// Resolves one line's product, converts its quantity to the base unit and validates the
    /// product's own quantity rule (FR-2.1-FR-2.8), through the same
    /// <see cref="Domain.Catalogue.UomConverter"/> every other UOM-aware path in this codebase
    /// uses - never a second, ad-hoc multiplication by the conversion factor.
    /// </summary>
    private async Task<PricedGoodsReceiptLine> PriceLineAsync(
        CreateGoodsReceiptLineCommand line,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Quantity <= 0m)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Variant {line.ProductVariantId}: the quantity received must be greater than zero."));
        }

        if (line.UnitCost.IsNegative)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Variant {line.ProductVariantId}: the unit cost cannot be negative."));
        }

        var tax = line.Tax ?? Money.Zero;
        if (tax.IsNegative)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Variant {line.ProductVariantId}: tax cannot be negative."));
        }

        var item = await _catalogue.FindByVariantIdAsync(line.ProductVariantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Product variant {line.ProductVariantId} does not exist."));

        var variant = await _products.FindVariantByIdAsync(line.ProductVariantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Product variant {line.ProductVariantId} does not exist."));

        var product = item.ToProduct();

        // AC-08: the one and only conversion of this line's quantity into the product's base
        // unit. Everything else this line produces is built from qtyBase, never from
        // line.Quantity again.
        var qtyBase = UomConverter.ToBase(line.Quantity, line.UomId, product);
        var qty = Quantity.FromDecimal(line.Quantity, line.UomId);
        var subtotal = line.UnitCost.Multiply(line.Quantity);

        return new PricedGoodsReceiptLine(item, variant.Sku, qty, qtyBase, line.UnitCost, subtotal, tax);
    }

    /// <summary>
    /// FR-2.18/FR-4.8: every line whose landed cost on this receipt exceeds the variant's current
    /// selling price, both compared per base unit.
    /// </summary>
    private static List<GoodsReceiptPriceReviewFlag> BuildPriceReviewFlags(
        IReadOnlyList<FinishedGoodsReceiptLine> lines)
    {
        var flags = new List<GoodsReceiptPriceReviewFlag>();

        foreach (var line in lines)
        {
            if (line.UnitCostBase <= line.CurrentSellingPrice)
            {
                continue;
            }

            flags.Add(new GoodsReceiptPriceReviewFlag(
                line.ProductVariantId,
                line.Sku,
                line.Description,
                line.UnitCostBase,
                line.CurrentSellingPrice));
        }

        return flags;
    }

    /// <summary>
    /// Shelf labels for the received batch (SRS FR-2.12, hooks into P1-T12's
    /// <see cref="ILabelPrintService"/>). One label copy per base unit received for a whole-unit
    /// product, so every physical piece that just arrived gets its own tag; a single label for a
    /// product measured in a fractional unit (metres, litres), since there is no meaning to
    /// "one label per 0.35 of a unit".
    /// </summary>
    private Task<PrintOutcome> PrintLabelsAsync(
        IReadOnlyList<FinishedGoodsReceiptLine> lines,
        CancellationToken cancellationToken)
    {
        var items = lines
            .Select(line => new LabelPrintRequestItem(line.ProductVariantId, LabelsFor(line)))
            .ToArray();

        return _labels.PrintAsync(items, cancellationToken);
    }

    private static int LabelsFor(FinishedGoodsReceiptLine line) =>
        line.ProductType == ProductType.Standard
            ? (int)Math.Max(1m, decimal.Truncate(line.QtyBase.Value))
            : 1;

    /// <summary>
    /// Asserts the goods-receipt identities before anything is written: every line's own total
    /// sums to the header total, and the header's subtotal/tax/freight add up to it too. Mirrors
    /// <c>CompleteSaleHandler.RequireBillBalances</c> - compared as scaled integers, over the
    /// values as they will be stored, so a stored row one scaled unit out never slips through a
    /// decimal comparison that happened to round the same way on both sides.
    /// </summary>
    private static void RequireReceiptBalances(
        Money subtotal,
        Money tax,
        Money otherCost,
        Money total,
        IReadOnlyList<FinishedGoodsReceiptLine> lines)
    {
        var lineTotals = lines.Aggregate(0L, (running, line) => running + line.LineTotal.ToScaled());
        var expected = subtotal.ToScaled() + tax.ToScaled() + otherCost.ToScaled();

        if (lineTotals != expected)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The receipt lines come to {Money.FromScaled(lineTotals)} but subtotal + tax + "
                + $"other cost comes to {Money.FromScaled(expected)}. They must match exactly."));
        }

        if (total.ToScaled() != expected)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The receipt total {total} does not equal subtotal + tax + other cost "
                + $"({Money.FromScaled(expected)})."));
        }
    }

    private AuthenticatedUser RequireSignedIn() =>
        _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A goods receipt records who received it, so sign in first.");

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string AuditPayload(string grnNo, string supplierName, Money total) =>
        SecurityAuditJson.Object(("grn_no", grnNo), ("supplier", supplierName), ("total", total.ToScaled()));

    /// <summary>One receipt line, priced and converted to base units, before freight is apportioned.</summary>
    private sealed record PricedGoodsReceiptLine(
        CatalogueItem Item,
        string Sku,
        Quantity Qty,
        Quantity QtyBase,
        Money UnitCost,
        Money Subtotal,
        Money Tax)
    {
        internal FinishedGoodsReceiptLine Finish(Money freightShare)
        {
            var landed = Subtotal + freightShare;
            var unitCostBase = landed.Divide(QtyBase.Value);
            var unitCostBaseExcludingFreight = Subtotal.Divide(QtyBase.Value);

            return new FinishedGoodsReceiptLine(
                Item.ProductVariantId,
                Item.ProductId,
                Item.ProductType,
                Sku,
                Item.Description,
                Item.UomSymbol,
                Item.UnitPrice,
                Qty,
                QtyBase,
                UnitCost,
                Subtotal,
                freightShare,
                Tax,
                landed + Tax,
                unitCostBase,
                unitCostBaseExcludingFreight);
        }
    }

    /// <summary>
    /// One receipt line with its freight share folded in - what the transaction actually writes.
    /// </summary>
    private sealed record FinishedGoodsReceiptLine(
        long ProductVariantId,
        long ProductId,
        ProductType ProductType,
        string Sku,
        string Description,
        string UomSymbol,
        Money CurrentSellingPrice,
        Quantity Qty,
        Quantity QtyBase,
        Money UnitCost,
        Money Subtotal,
        Money FreightShare,
        Money Tax,
        Money LineTotal,
        Money UnitCostBase,
        Money UnitCostBaseExcludingFreight)
    {
        internal NewGoodsReceiptLine ToNewLine() => new(
            ProductVariantId, Qty, QtyBase, UnitCost, UnitCostBase, Tax, LineTotal);

        internal GoodsReceiptDocumentLine ToDocumentLine() => new(
            Sku, Description, Qty, UomSymbol, UnitCost, Tax, LineTotal);
    }
}
