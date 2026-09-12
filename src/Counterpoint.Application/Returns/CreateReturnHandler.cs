using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// The return commit, the same shape <c>CompleteSaleHandler</c> gives a bill:
///
/// <code>
/// BEGIN IMMEDIATE
///   allocate return_no from number_sequence
///   insert sale_return (+ prev_hash / row_hash)
///   insert sale_return_line[]
///   increment sale_line.qty_returned[]      -- the one permitted update, guard checked first
///   post RETURN_IN movements                -- SELLABLE lines only
///   insert refund payment (negative amount)
///   insert audit_log
///   insert print_job                        -- outbox, not a printer call
/// COMMIT
/// </code>
///
/// If anything in that block throws, nothing happened except a consumed return number - which is
/// correct and auditable, the same as a failed sale (SRS FR-3.30's return-side cousin).
/// </summary>
/// <remarks>
/// <para>
/// <b>Refunding at today's price is structurally impossible from here.</b> This handler never
/// calls <see cref="IProductLookup"/> or any other catalogue price read - the only prices it ever
/// sees are the ones already snapshotted onto <c>sale_line</c> and handed back by
/// <see cref="IReturnableSaleLookup"/> (CLAUDE.md invariant 10, SRS AC-03). There is no current
/// price anywhere in this file to refund at by mistake.
/// </para>
/// <para>
/// <b>AC-06 is enforced twice, on purpose.</b>
/// <see cref="IReturnPolicyAuthorisationService.AuthoriseCumulativeQuantity"/> is checked for every
/// line before the transaction opens, so a cashier gets the plain-language reason
/// (<see cref="ReturnNotEligibleException"/>) rather than a bare constraint-violation message. The
/// database's own <c>trg_sale_line_qty_returned_bounds</c> is the backstop that makes the limit
/// true even if this application-layer check were ever bypassed or a future caller forgot it -
/// there is no override parameter on that authorisation call anywhere in this codebase (task
/// P2-T01 risk note), so the two checks can never be made to disagree by design.
/// </para>
/// <para>
/// <b><c>DAMAGED</c> lines post no stock movement at all.</b> <c>StockLedgerMath.Apply</c> has no
/// notion of a "damaged bucket" distinct from <c>stock_balance.qty_base</c> - a movement's
/// quantity always changes that one projected number, for every movement type alike. A returned
/// item that is quarantined as damaged was never added back to sellable stock in the first place
/// (SRS FR-5.8: "not added back to sellable stock"), so the sellable balance is already correct
/// exactly as it stood after the original sale - there is no stock change for
/// <see cref="IStockLedger.PostAsync"/> to record. Posting a balancing
/// <c>RETURN_IN</c> + <c>DAMAGE</c> pair to manufacture a ledger entry was considered and
/// rejected: outbound movements value at whatever <c>cost_avg</c> the inbound half just
/// recomputed (<c>StockLedgerMath.Apply</c>'s documented behaviour), so a pair like that quietly
/// drags the moving-average cost of every <em>other</em> unit of the same variant toward the
/// damaged unit's original cost, even though those other units never moved. The durable,
/// queryable record of a damaged disposition is <c>sale_return_line.disposition = 'DAMAGED'</c>
/// itself, valued at <see cref="ReturnableSaleLine.UnitCost"/> - enough for a future shrinkage
/// report to total without perturbing anyone's stock valuation. Given <c>sale_return_line</c>
/// already carries every field such a report needs, and that a genuine "damaged-stock account"
/// distinct from <c>stock_balance</c> would need either a schema addition or an
/// <see cref="IStockLedger"/> contract change neither the schema nor this task's brief calls for,
/// P2-T08 ("Adjustments and damage", which already owns <c>DAMAGE</c>-movement semantics and
/// shrinkage reporting) is the natural owner if a ledger-level view is wanted later.
/// </para>
/// </remarks>
public sealed class CreateReturnHandler : ICreateReturn
{
    /// <summary>The <c>number_sequence.doc_type</c> a return is numbered from.</summary>
    private const string ReturnDocumentType = "RETURN";

    /// <summary>The <c>stock_movement.movement_type</c> and <c>ref_doc_type</c> a SELLABLE line posts.</summary>
    private const string ReturnInMovementType = "RETURN_IN";

    private const string ReturnAuditAction = "RETURN_COMPLETED";
    private const string CompletedSaleStatus = "COMPLETED";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IReturnableSaleLookup _sales;
    private readonly ISaleReturnWriter _returns;
    private readonly IStockLedger _stock;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly IReturnReceiptRenderer _receipts;
    private readonly IRoundingPolicy _rounding;
    private readonly ISession _session;
    private readonly IReturnPolicyAuthorisationService _policy;
    private readonly ISettings _settings;
    private readonly ICategoryStore _categories;

    public CreateReturnHandler(
        IUnitOfWork unitOfWork,
        IDocumentNumberAllocator numbers,
        IReturnableSaleLookup sales,
        ISaleReturnWriter returns,
        IStockLedger stock,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        IReturnReceiptRenderer receipts,
        IRoundingPolicy rounding,
        ISession session,
        IReturnPolicyAuthorisationService policy,
        ISettings settings,
        ICategoryStore categories)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(sales);
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(rounding);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(categories);

        _unitOfWork = unitOfWork;
        _numbers = numbers;
        _sales = sales;
        _returns = returns;
        _stock = stock;
        _audit = audit;
        _printJobs = printJobs;
        _receipts = receipts;
        _rounding = rounding;
        _session = session;
        _policy = policy;
        _settings = settings;
        _categories = categories;
    }

    /// <inheritdoc />
    public async Task<CreatedReturn> CreateAsync(
        CreateReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAtLeastOneLine(command);
        RequireNoDuplicateLines(command);
        RequireSupportedRefundMethod(command.RefundMethod);

        var seller = RequireTheSellerIsSignedIn(command);

        // Read before the transaction opens - the same NFR-P3 discipline CompleteSaleHandler
        // keeps for pricing: the writer lock is for the write alone.
        var sale = await _sales.FindBySaleIdAsync(command.OriginalSaleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {command.OriginalSaleId} was not found."));

        RequireSaleIsReturnable(sale);

        // Policy checks that apply to the whole return, not to one line - evaluated before a
        // single line is priced, the same order the return screen would walk a cashier through.
        _policy.AuthoriseReturnWindow(sale.SoldAt, command.ReturnWindowOverride);
        _policy.AuthoriseReceiptRequirement(command.BillReferencePresented, command.ReceiptRequirementOverride);

        var priced = PriceReturn(sale, command);

        var isCashRefund = command.RefundMethod == RefundMethod.Cash;
        _policy.AuthoriseCashRefundLimit(isCashRefund, priced.TotalRefund, command.CashRefundLimitOverride);

        var policyText = await BuildPolicyTextAsync(cancellationToken).ConfigureAwait(false);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var returnNo = await _numbers
                    .AllocateAsync(ReturnDocumentType, sale.BusinessDate, token)
                    .ConfigureAwait(false);

                var saleReturnId = await _returns.InsertSaleReturnAsync(
                    new NewSaleReturn(
                        returnNo,
                        sale.SaleId,
                        command.ReturnedAt,
                        sale.BusinessDate,
                        sale.CustomerId,
                        command.UserId,
                        command.ShiftId,
                        priced.Subtotal,
                        priced.Tax,
                        priced.RestockingFee,
                        priced.TotalRefund,
                        RefundMethodToken(command.RefundMethod),
                        AuthorisedBy(command),
                        command.Reason),
                    token).ConfigureAwait(false);

                foreach (var line in priced.Lines)
                {
                    await _returns.InsertSaleReturnLineAsync(saleReturnId, line.ToNewSaleReturnLine(), token)
                        .ConfigureAwait(false);

                    // The one permitted update on sale_line (CLAUDE.md invariant 5). The guard
                    // above already refused anything this would push over qty_base; the trigger
                    // is the backstop that makes it true regardless.
                    await _returns.IncrementQtyReturnedAsync(line.SaleLineId, line.QuantityBase, token)
                        .ConfigureAwait(false);

                    // Open-item lines (no ProductVariantId) and DAMAGED lines post no stock
                    // movement at all - see the class remarks for why DAMAGED is deliberately
                    // silent here rather than a ledger entry.
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
                                command.ReturnedAt),
                            token).ConfigureAwait(false);
                    }
                }

                // One payment row for the whole refund (negative amount, sale_id left null so
                // ck_payment_one_document holds) - split refunds across several tenders are not
                // asked for by this task.
                await _returns.InsertRefundPaymentAsync(
                    saleReturnId,
                    new NewTender(
                        RefundMethodToTenderType(command.RefundMethod),
                        priced.TotalRefund.Negate(),
                        null,
                        command.ReturnedAt),
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.ReturnedAt,
                        command.UserId,
                        ReturnAuditAction,
                        "sale_return",
                        saleReturnId,
                        AfterJson: AuditPayload(returnNo, priced.TotalRefund),
                        Reason: command.Reason),
                    token).ConfigureAwait(false);

                // Rendered here, inside the transaction, for the same reason the sale receipt is
                // (CLAUDE.md invariant 7): the return number only exists once number_sequence has
                // been read, and this is a pure in-memory byte transform - no device, no I/O.
                var payload = _receipts.Render(priced.ToReceipt(
                    returnNo,
                    sale.BillNo,
                    command.ReturnedAt,
                    RefundMethodToken(command.RefundMethod),
                    seller.DisplayName,
                    policyText));

                var printJobId = await _printJobs
                    .EnqueueAsync(new PrintJobRequest(ReturnDocumentType, saleReturnId, payload), token)
                    .ConfigureAwait(false);

                return new CreatedReturn(saleReturnId, returnNo, priced.TotalRefund, printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prices every requested line against the original bill: the cumulative over-return guard
    /// (AC-06) and the non-returnable check (AC-05) first, then the refund itself, prorated from
    /// the original line's own totals so a partial return of a discounted line refunds exactly
    /// its share of the discount (AC-03) - never <see cref="ReturnableSaleLine.UnitPrice"/>
    /// multiplied fresh by the requested quantity, which would silently drop it.
    /// </summary>
    private PricedReturn PriceReturn(ReturnableSale sale, CreateReturnCommand command)
    {
        var linesBySaleLineId = sale.Lines.ToDictionary(line => line.SaleLineId);

        var lines = new List<PricedReturnLine>(command.Lines.Count);
        var subtotal = Money.Zero;
        var tax = Money.Zero;

        foreach (var request in command.Lines)
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
                    nameof(command), request.QuantityBase.Value, "A return line must ask for a positive quantity.");
            }

            // Re-tagged against the original line's own quantities, not trusted from the caller:
            // Quantity's uom tag is IReturnableSaleLookup's own bookkeeping convenience (see its
            // remarks), private to this port and its writer, and a caller building
            // ReturnLineRequest has no way to know it - only the numeric value is theirs to give.
            var requestedQuantity = Quantity.FromDecimal(request.QuantityBase.Value, original.QtySoldBase.UomId);

            // AC-06, never overridable - see the class remarks.
            _policy.AuthoriseCumulativeQuantity(
                original.QtySoldBase, original.QtyReturnedBase, requestedQuantity);

            // AC-05: a non-returnable product or category needs an owner override; a returnable
            // one sails through untouched.
            _policy.AuthoriseNonReturnable(
                original.NonReturnable, original.CategoryId, command.NonReturnableOverride);

            var ratio = requestedQuantity.Value / original.QtySoldBase.Value;

            // Rounding point one, for this document: the line's own refund (mirrors
            // sale_line.line_total's own rounding when the bill was completed).
            var lineRefund = _rounding.Round(original.LineTotal * ratio);

            // Quantised to the storage scale only, exactly as CompleteSaleHandler leaves a line's
            // tax - not an independent rounding point (CLAUDE.md invariant 2).
            var lineTax = Money.FromScaled((original.Tax * ratio).ToScaled());

            subtotal += lineRefund;
            tax += lineTax;

            lines.Add(new PricedReturnLine(
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

        // The one restocking-fee accessor the policy engine offers (task P2-T01's own remarks on
        // ReturnPolicyTextFormatter), applied to the merchandise value alone - never to tax.
        var fee = _rounding.Round(_settings.Policy.RestockingFeeRate.Of(subtotal));

        // Exactly the sum of already-rounded/quantised parts, not a further rounding point:
        // sale_return carries no residual "rounding" column the way sale does, so this identity
        // has to hold by construction rather than by a third rounding call papering over it.
        var totalRefund = subtotal + tax - fee;

        return new PricedReturn(subtotal, tax, fee, totalRefund, lines);
    }

    private async Task<string> BuildPolicyTextAsync(CancellationToken cancellationToken)
    {
        var policy = _settings.Policy;
        var names = new List<string>(policy.NonReturnableCategoryIds.Count);

        foreach (var categoryId in policy.NonReturnableCategoryIds)
        {
            var category = await _categories.FindByIdAsync(categoryId, cancellationToken).ConfigureAwait(false);
            if (category is not null)
            {
                names.Add(category.Name);
            }
        }

        return ReturnPolicyTextFormatter.Describe(policy, names);
    }

    private static void RequireAtLeastOneLine(CreateReturnCommand command)
    {
        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new InvalidOperationException("A return must have at least one line.");
        }
    }

    private static void RequireNoDuplicateLines(CreateReturnCommand command)
    {
        var seen = new HashSet<long>();
        foreach (var line in command.Lines)
        {
            if (!seen.Add(line.SaleLineId))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bill line {line.SaleLineId} appears twice in the same return. Combine it into one line."));
            }
        }
    }

    private static void RequireSupportedRefundMethod(RefundMethod refundMethod)
    {
        if (refundMethod == RefundMethod.CreditNote)
        {
            throw new InvalidOperationException(
                "Refunding by store credit needs a credit note to issue it against, and issuing "
                + "credit notes is P2-T05. Refund by cash or card for now.");
        }
    }

    private static void RequireSaleIsReturnable(ReturnableSale sale)
    {
        if (!string.Equals(sale.Status, CompletedSaleStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Bill {sale.BillNo} is {sale.Status}, not completed, and has nothing left to return."));
        }
    }

    /// <summary>
    /// Asserts that the person taking the return is the person it will be stamped with (SRS
    /// FR-1.1, FR-1.6) - the same guard <c>CompleteSaleHandler</c> keeps for a sale.
    /// </summary>
    private AuthenticatedUser RequireTheSellerIsSignedIn(CreateReturnCommand command)
    {
        var seller = _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A return records who took it, so sign in before taking one.");

        if (seller.Id != command.UserId)
        {
            throw new InvalidOperationException(
                "This shift was opened by someone else. Close it and open a new one, so the return "
                + "records who actually took it.");
        }

        return seller;
    }

    /// <summary>
    /// The owner who granted whichever override this return actually needed, or null when none
    /// did. Every <see cref="OverrideToken"/> the caller supplied was already checked against its
    /// own rule above; the one (if any) that came back <see cref="OverrideToken.IsConsumed"/> is
    /// the one this return was authorised by.
    /// </summary>
    private static long? AuthorisedBy(CreateReturnCommand command) =>
        new[]
        {
            command.ReturnWindowOverride,
            command.NonReturnableOverride,
            command.ReceiptRequirementOverride,
            command.CashRefundLimitOverride,
        }
        .FirstOrDefault(token => token is { IsConsumed: true })
        ?.GrantedByUserId;

    private static string RefundMethodToken(RefundMethod method) => method switch
    {
        RefundMethod.Cash => "CASH",
        RefundMethod.Card => "CARD",
        RefundMethod.CreditNote => throw new InvalidOperationException("Credit note refunds are P2-T05."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };

    private static string RefundMethodToTenderType(RefundMethod method) => method switch
    {
        RefundMethod.Cash => TenderTypes.Cash,
        RefundMethod.Card => TenderTypes.Card,
        RefundMethod.CreditNote => throw new InvalidOperationException("Credit note refunds are P2-T05."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };

    /// <summary>
    /// The audit row's after-state. Written by hand rather than serialised so the text is stable
    /// byte for byte - it is about to be hashed into a chain.
    /// </summary>
    private static string AuditPayload(string returnNo, Money totalRefund) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"return_no":"{{returnNo}}","total_refund":{{totalRefund.ToScaled()}}}""");

    /// <summary>A return, priced and checked, ready to be written.</summary>
    private sealed record PricedReturn(
        Money Subtotal,
        Money Tax,
        Money RestockingFee,
        Money TotalRefund,
        IReadOnlyList<PricedReturnLine> Lines)
    {
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
                [.. Lines.Select(line => new SaleReturnReceiptLine(
                    line.Description,
                    line.QuantityBase,
                    line.UomSymbol,
                    line.UnitPrice,
                    line.LineRefund,
                    ReturnDispositions.ToToken(line.Disposition)))],
                Subtotal,
                Tax,
                RestockingFee,
                TotalRefund,
                refundMethodToken,
                cashierName,
                policyText);
    }

    /// <summary>One priced return line.</summary>
    private sealed record PricedReturnLine(
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
    }
}
