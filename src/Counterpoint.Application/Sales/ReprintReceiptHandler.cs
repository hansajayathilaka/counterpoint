using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Reprints any past bill: rendered fresh from the stored rows, marked <c>DUPLICATE</c>, queued
/// through the same outbox a first print uses, and logged (SRS FR-3.36, FR-7.5, FR-7.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>A fresh render, not a byte-for-byte replay.</b> <c>print_job.payload</c> for the original
/// sale is never read back: the bill is reassembled from <c>sale</c>, <c>sale_line</c> and
/// <c>payment</c> through <see cref="ISaleReceiptLookup"/> and rendered through the same
/// <see cref="ISaleReceiptRenderer"/> a first print uses, with <c>isDuplicate: true</c>. A shop
/// that has since changed its receipt template or shop name gets a duplicate that looks like
/// today's bills, marked DUPLICATE, not a museum piece of whatever the template said on the day
/// of sale.
/// </para>
/// <para>
/// The queue-and-audit write is its own transaction, not folded into any other - a reprint is not
/// part of the original sale (CLAUDE.md invariant 5: <c>sale</c> is append-only and this touches
/// none of it) and needs no writer lock held any longer than the row insert takes (NFR-P3).
/// </para>
/// </remarks>
public sealed class ReprintReceiptHandler : IReprintReceipt
{
    private const string ReprintedAuditAction = "RECEIPT_REPRINTED";
    private const string SaleEntityType = "sale";
    private const string SaleDocType = "SALE";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISaleReceiptLookup _receipts;
    private readonly ISaleReceiptRenderer _renderer;
    private readonly IPrintJobOutbox _printJobs;
    private readonly IAuditTrail _audit;
    private readonly ISession _session;
    private readonly ISettings _settings;
    private readonly TimeProvider _timeProvider;

    public ReprintReceiptHandler(
        IUnitOfWork unitOfWork,
        ISaleReceiptLookup receipts,
        ISaleReceiptRenderer renderer,
        IPrintJobOutbox printJobs,
        IAuditTrail audit,
        ISession session,
        ISettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _receipts = receipts;
        _renderer = renderer;
        _printJobs = printJobs;
        _audit = audit;
        _session = session;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<ReprintedReceipt> ReprintAsync(long saleId, CancellationToken cancellationToken = default)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A reprint records who asked for it, so sign in before reprinting a bill.");

        // Read before the transaction opens - the same NFR-P3 discipline every other handler in
        // this file keeps. sale, sale_line and payment are all append-only, so nothing read here
        // can go stale before the write below.
        var receipt = await _receipts.FindReceiptAsync(saleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {saleId} was not found."));

        var payload = _renderer.Render(receipt, isDuplicate: true);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var printJobId = await _printJobs
                    .EnqueueAsync(
                        new PrintJobRequest(
                            SaleDocType,
                            saleId,
                            payload,
                            Copies: _settings.Peripherals.ReceiptCopies,
                            IsDuplicate: true),
                        token)
                    .ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        _timeProvider.GetLocalNow(),
                        actor.Id,
                        ReprintedAuditAction,
                        SaleEntityType,
                        saleId,
                        AfterJson: AuditPayload(receipt.BillNo)),
                    token).ConfigureAwait(false);

                return new ReprintedReceipt(saleId, receipt.BillNo, printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The audit row's after-state. Written by hand so the text is stable byte for byte.</summary>
    private static string AuditPayload(string billNo) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"bill_no":"{{billNo}}"}""");
}
