using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Purchasing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Purchasing;

/// <summary>The owner's purchase-order service (SRS FR-4.5, FR-4.6, FR-4.10).</summary>
/// <remarks>
/// Internal, for the same reason every other owner-only Application service is
/// (<see cref="Counterpoint.Application.Catalogue.SupplierMaintenanceService"/>'s own remarks):
/// the role check on <see cref="IPurchaseOrderService"/> only holds if nothing outside this
/// assembly can construct the class the check is supposed to be in front of.
/// </remarks>
internal sealed class PurchaseOrderService : IPurchaseOrderService
{
    /// <summary>The <c>number_sequence.doc_type</c> a purchase order is numbered from.</summary>
    private const string PurchaseOrderDocumentType = "PO";

    private readonly IPurchaseOrderStore _store;
    private readonly ISupplierStore _suppliers;
    private readonly IProductLookup _catalogue;
    private readonly IProductStore _products;
    private readonly ISuggestedOrderQuery _suggestedOrder;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly IPurchaseOrderDocumentRenderer _renderer;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public PurchaseOrderService(
        IPurchaseOrderStore store,
        ISupplierStore suppliers,
        IProductLookup catalogue,
        IProductStore products,
        ISuggestedOrderQuery suggestedOrder,
        IDocumentNumberAllocator numbers,
        IUnitOfWork unitOfWork,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        IPurchaseOrderDocumentRenderer renderer,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(suggestedOrder);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _suppliers = suppliers;
        _catalogue = catalogue;
        _products = products;
        _suggestedOrder = suggestedOrder;
        _numbers = numbers;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _printJobs = printJobs;
        _renderer = renderer;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PurchaseOrderSummaryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<PurchaseOrderRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _store.FindByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseOrderRecord> CreateAsync(
        CreatePurchaseOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireSignedIn();

        var supplier = await _suppliers.FindByIdAsync(command.SupplierId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is no supplier with id {command.SupplierId}."));

        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new InvalidOperationException("A purchase order needs at least one line.");
        }

        var lines = new List<NewPurchaseOrderLine>(command.Lines.Count);
        foreach (var line in command.Lines)
        {
            lines.Add(await ValidateLineAsync(line, cancellationToken).ConfigureAwait(false));
        }

        var orderedAt = _timeProvider.GetLocalNow();
        var businessDate = DateOnly.FromDateTime(orderedAt.Date);
        var note = Trimmed(command.Note);

        var record = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var poNo = await _numbers.AllocateAsync(PurchaseOrderDocumentType, businessDate, token)
                    .ConfigureAwait(false);

                var id = await _store.CreateAsync(
                    new NewPurchaseOrder(poNo, command.SupplierId, orderedAt, command.ExpectedAt, actor.Id, note, lines),
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        orderedAt,
                        actor.Id,
                        PurchaseOrderAuditActions.Created,
                        PurchaseOrderAuditActions.EntityType,
                        id,
                        AfterJson: AuditPayload(poNo, PurchaseOrderStatuses.DraftToken, supplier.Name)),
                    token).ConfigureAwait(false);

                return await _store.FindByIdAsync(id, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return record ?? throw new InvalidOperationException(
            "The purchase order was created but could not be read back.");
    }

    /// <inheritdoc />
    public async Task SendAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = RequireSignedIn();
        var order = await RequireOrderAsync(id, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(order.Status, PurchaseOrderStatuses.DraftToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"Purchase order {order.PoNo} is {order.Status}, not draft, and cannot be sent again."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateStatusAsync(id, PurchaseOrderStatus.Sent, token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        actor.Id,
                        PurchaseOrderAuditActions.Sent,
                        PurchaseOrderAuditActions.EntityType,
                        id,
                        BeforeJson: AuditPayload(order.PoNo, order.Status, order.SupplierName),
                        AfterJson: AuditPayload(order.PoNo, PurchaseOrderStatuses.SentToken, order.SupplierName)),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(long id, string reason, CancellationToken cancellationToken = default)
    {
        var actor = RequireSignedIn();
        var order = await RequireOrderAsync(id, cancellationToken).ConfigureAwait(false);

        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
        {
            throw new InvalidOperationException("Cancelling a purchase order needs a reason.");
        }

        var status = PurchaseOrderStatuses.Parse(order.Status);
        if (PurchaseOrderStatuses.IsTerminal(status))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"Purchase order {order.PoNo} is already {order.Status} and cannot be cancelled."));
        }

        var now = _timeProvider.GetLocalNow();

        // Deliberately nothing here touches stock: no purchase order line has ever posted a
        // movement (P2-T07's GRN is the only door stock is allowed through, CLAUDE.md invariant
        // 3), so cancelling before or after some quantity was received changes purchase_order.status
        // alone.
        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateStatusAsync(id, PurchaseOrderStatus.Cancelled, token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        actor.Id,
                        PurchaseOrderAuditActions.Cancelled,
                        PurchaseOrderAuditActions.EntityType,
                        id,
                        BeforeJson: AuditPayload(order.PoNo, order.Status, order.SupplierName),
                        AfterJson: SecurityAuditJson.Object(
                            ("po_no", order.PoNo),
                            ("status", PurchaseOrderStatuses.CancelledToken),
                            ("reason", trimmedReason))),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> PrintAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = RequireSignedIn();
        var order = await RequireOrderAsync(id, cancellationToken).ConfigureAwait(false);
        var supplier = await _suppliers.FindByIdAsync(order.SupplierId, cancellationToken).ConfigureAwait(false);

        var document = new PurchaseOrderDocument(
            order.PoNo,
            order.OrderedAt,
            order.ExpectedAt,
            order.SupplierName,
            supplier?.Address,
            supplier?.Phone,
            order.Status,
            actor.DisplayName,
            order.Note,
            [.. order.Lines.Select(line => new PurchaseOrderDocumentLine(
                line.Sku, line.ProductDescription, line.Qty, line.UomSymbol, line.UnitCost, line.LineTotal))]);

        // A pure in-memory byte transform, rendered outside any transaction - the same split
        // CompleteSaleHandler draws between pricing (no I/O) and posting (the transaction), except
        // here the render happens before the enqueue rather than inside it, because nothing about
        // printing a purchase order needs to be atomic with anything else (CLAUDE.md invariant 7:
        // no printer call is ever made inside a transaction - and a purchase order print job has
        // no sibling writes to be atomic with in the first place).
        var payload = _renderer.RenderPdf(document);

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var printJobId = await _printJobs
                    .EnqueueAsync(new PrintJobRequest("PO", id, payload), token)
                    .ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        actor.Id,
                        PurchaseOrderAuditActions.Printed,
                        PurchaseOrderAuditActions.EntityType,
                        id,
                        AfterJson: SecurityAuditJson.Object(("po_no", order.PoNo), ("print_job_id", printJobId))),
                    token).ConfigureAwait(false);

                return printJobId;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecomputeStatusAsync(long id, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(id, cancellationToken).ConfigureAwait(false);
        var currentStatus = PurchaseOrderStatuses.Parse(order.Status);

        // Receipt progress drives exactly the middle of the lifecycle (P2-T06 "Do this" #2):
        // DRAFT and CANCELLED are explicit operator actions this recomputation has no business
        // overriding, and RECEIVED is already terminal.
        if (currentStatus is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
        {
            return;
        }

        var progress = await _store.FindReceiptProgressAsync(id, cancellationToken).ConfigureAwait(false);
        var newStatus = PurchaseOrderStatusCalculator.DeriveFromReceiptProgress(progress);

        if (newStatus == currentStatus)
        {
            return;
        }

        var actor = _session.CurrentUser;
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateStatusAsync(id, newStatus, token).ConfigureAwait(false);

                if (actor is not null)
                {
                    await _audit.RecordAsync(
                        new AuditEntry(
                            now,
                            actor.Id,
                            PurchaseOrderAuditActions.StatusRecomputed,
                            PurchaseOrderAuditActions.EntityType,
                            id,
                            BeforeJson: AuditPayload(order.PoNo, order.Status, order.SupplierName),
                            AfterJson: AuditPayload(order.PoNo, PurchaseOrderStatuses.ToToken(newStatus), order.SupplierName)),
                        token).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SuggestedOrderLine>> GetSuggestedOrderAsync(CancellationToken cancellationToken = default) =>
        _suggestedOrder.GetSuggestedOrderAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductUomRecord>> GetOrderingUnitsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        var variant = await _products.FindVariantByIdAsync(productVariantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"Product variant {productVariantId} does not exist."));

        return await _products.ListUomOptionsAsync(variant.ProductId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the product a line names and checks that its quantity is one the named unit
    /// allows (FR-2.1-FR-2.8), the same rule <c>CompleteSaleHandler</c> enforces on a bill line -
    /// via the same <see cref="UomConverter"/>, even though a purchase order line stores its
    /// quantity in the ordering unit rather than in base units (P2-T06's own note: converting to
    /// base units happens at goods receipt, P2-T07, not here).
    /// </summary>
    private async Task<NewPurchaseOrderLine> ValidateLineAsync(
        CreatePurchaseOrderLineCommand line,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Quantity <= 0m)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"Variant {line.ProductVariantId}: the quantity ordered must be greater than zero."));
        }

        if (line.UnitCost.IsNegative)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"Variant {line.ProductVariantId}: the unit cost cannot be negative."));
        }

        var item = await _catalogue.FindByVariantIdAsync(line.ProductVariantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"Product variant {line.ProductVariantId} does not exist."));

        var product = item.ToProduct();

        // Discarded on purpose - this call's whole job here is validation (the unit belongs to
        // the product, and the quantity is one that unit's own decimal-place rule allows). The
        // base-unit conversion this returns is not stored on a purchase order line at all.
        _ = UomConverter.ToBase(line.Quantity, line.UomId, product);

        return new NewPurchaseOrderLine(line.ProductVariantId, Quantity.FromDecimal(line.Quantity, line.UomId), line.UnitCost);
    }

    private async Task<PurchaseOrderRecord> RequireOrderAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no purchase order with id {id}."));

    private AuthenticatedUser RequireSignedIn() =>
        _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A purchase order records who raised it, so sign in first.");

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string AuditPayload(string poNo, string status, string supplierName) =>
        SecurityAuditJson.Object(("po_no", poNo), ("status", status), ("supplier", supplierName));
}
