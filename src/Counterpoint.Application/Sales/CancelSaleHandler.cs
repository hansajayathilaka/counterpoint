using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Cancels a completed bill: owner-only, same business day only, a reason is required, stock is
/// reversed by compensating movements, and the bill keeps its number (SRS FR-3.34).
/// </summary>
/// <remarks>
/// <para>
/// <c>internal</c>, exactly like <c>CompleteSaleHandler</c> is not - it does not need to be,
/// because nothing else in the Application layer calls this one directly - and every other
/// concrete owner-only service in this codebase (<c>UomMaintenanceService</c> and its siblings):
/// the composition root wraps it in <see cref="RoleAuthorisation.Decorate{TService}"/> and only
/// ever hands out <see cref="ICancelSale"/>, so a caller cannot reach an undecorated instance
/// (CLAUDE.md invariant 8, SRS NFR-S2, AC-17).
/// </para>
/// <para>
/// <b>Reversal, never deletion.</b> The original <c>sale</c> and <c>sale_line</c> rows are never
/// touched beyond the one column-scoped update the database itself permits
/// (<c>ISaleWriter.CancelSaleAsync</c>). Stock comes back through the same
/// <c>IStockLedger.PostAsync</c> door every other stock change goes through (CLAUDE.md
/// invariant 3) - one compensating, positive movement per original outbound one, at the same
/// cost, referencing the same bill.
/// </para>
/// </remarks>
internal sealed class CancelSaleHandler : ICancelSale
{
    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> the reversal uses
    /// - the same ones the original sale posted (docs/01_DATA_MODEL.md §4): a compensating pair
    /// against the one bill, not a new document type the schema has no room for.</summary>
    private const string SaleMovementType = "SALE";

    private const string CompletedStatus = "COMPLETED";

    private const string CancelAuditAction = "SALE_CANCELLED";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISaleLookup _saleLookup;
    private readonly ISaleWriter _sales;
    private readonly IStockLedger _stock;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly ISaleCancellationReceiptRenderer _receipts;
    private readonly ISession _session;

    public CancelSaleHandler(
        IUnitOfWork unitOfWork,
        ISaleLookup saleLookup,
        ISaleWriter sales,
        IStockLedger stock,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        ISaleCancellationReceiptRenderer receipts,
        ISession session)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(saleLookup);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(session);

        _unitOfWork = unitOfWork;
        _saleLookup = saleLookup;
        _sales = sales;
        _stock = stock;
        _audit = audit;
        _printJobs = printJobs;
        _receipts = receipts;
        _session = session;
    }

    /// <inheritdoc />
    public async Task<CancelledSale> CancelAsync(
        CancelSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new InvalidOperationException(
                "Cancelling a bill needs a reason (SRS FR-3.34).");
        }

        var canceller = _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A cancellation records who authorised it, so sign in before cancelling a bill.");

        // Read before the transaction opens - the same NFR-P3 discipline CompleteSaleHandler
        // keeps for pricing. sale and stock_movement are both append-only, so nothing read here
        // can go stale before the write below.
        var sale = await _saleLookup.FindForCancellationAsync(command.SaleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {command.SaleId} was not found."));

        if (!string.Equals(sale.Status, CompletedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {sale.BillNo} is already cancelled."));
        }

        var cancelledOn = DateOnly.FromDateTime(command.CancelledAt.Date);
        if (sale.BusinessDate != cancelledOn)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {sale.BillNo} was sold on {sale.BusinessDate:yyyy-MM-dd} and can only be cancelled on the same business day (SRS FR-3.34)."));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _sales.CancelSaleAsync(sale.Id, canceller.Id, command.CancelledAt, token)
                    .ConfigureAwait(false);

                // One compensating movement per original one - the same door every stock change
                // goes through (CLAUDE.md invariant 3), never a raw UPDATE and never a delete.
                foreach (var movement in sale.StockMovements)
                {
                    await _stock.PostAsync(
                        new StockPosting(
                            movement.ProductVariantId,
                            SaleMovementType,
                            movement.QuantityBase,
                            movement.UnitCost,
                            SaleMovementType,
                            sale.Id,
                            canceller.Id,
                            command.CancelledAt,
                            "Cancellation of " + sale.BillNo),
                        token).ConfigureAwait(false);
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.CancelledAt,
                        canceller.Id,
                        CancelAuditAction,
                        "sale",
                        sale.Id,
                        AfterJson: AuditPayload(sale.BillNo, sale.Total),
                        Reason: command.Reason),
                    token).ConfigureAwait(false);

                // Rendered here, inside the transaction, for the same reason the sale receipt is:
                // the outbox row must carry the finished stream, and this is a pure in-memory
                // byte transform - no device, no I/O (CLAUDE.md invariant 7).
                var payload = _receipts.Render(new SaleCancellationReceipt(
                    sale.BillNo,
                    sale.SoldAt,
                    command.CancelledAt,
                    sale.Total,
                    command.Reason,
                    canceller.DisplayName));

                var printJobId = await _printJobs
                    .EnqueueAsync(new PrintJobRequest("SALE", sale.Id, payload), token)
                    .ConfigureAwait(false);

                return new CancelledSale(sale.Id, sale.BillNo, sale.StockMovements.Count, printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The audit row's after-state. Written by hand, exactly like <c>CompleteSaleHandler</c>'s,
    /// so the text is stable byte for byte - it is about to be hashed into a chain.
    /// </summary>
    private static string AuditPayload(string billNo, Money total) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"bill_no":"{{billNo}}","total":{{total.ToScaled()}}}""");
}
