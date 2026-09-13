using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// The unlinked-return commit (SRS FR-5.19, task P2-T03) - the same transaction shape
/// <see cref="CreateReturnHandler"/> gives a linked one, minus everything that only makes sense
/// with an original bill to read:
///
/// <code>
/// BEGIN IMMEDIATE
///   allocate return_no from number_sequence
///   insert sale_return (original_sale_id NULL, authorised_by the owner who granted the override)
///   insert sale_return_line[]              -- sale_line_id NULL on every line
///   post RETURN_IN movements                -- SELLABLE lines only, at TODAY's cost_avg
///   insert refund payment (negative amount)
///   insert audit_log, action = UNLINKED_RETURN
///   insert print_job                        -- outbox, not a printer call
/// COMMIT
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>The override is never optional here.</b>
/// <see cref="IReturnPolicyAuthorisationService.AuthoriseUnlinkedReturn"/> is called with
/// <see cref="CreateUnlinkedReturnCommand.Override"/>, which the command's own constructor already
/// requires to be non-null - there is no code path through this handler that reaches the
/// transaction without a granted, unexpired, unspent <see cref="OverrideToken"/> for
/// <see cref="ReturnPolicyAuditActions.UnlinkedReturn"/>. Obtaining that token
/// (<c>IOwnerOverrideService.RequestAsync</c>) itself refuses a blank reason and re-authenticates
/// an owner - so "requires owner re-authentication and a typed reason" (task P2-T03 step 2) is
/// true twice over: once for the override grant itself, and again for
/// <see cref="CreateUnlinkedReturnCommand.Reason"/>, which this handler separately refuses to be
/// blank before it ever reaches <c>sale_return.reason</c>.
/// </para>
/// <para>
/// <b>Refunding above today's price is structurally impossible from here.</b> Every line's
/// <see cref="UnlinkedReturnLineRequest.UnitPrice"/> is checked against
/// <see cref="CatalogueItem.UnitPrice"/>, read fresh from <see cref="IProductLookup"/> for this
/// return and never cached from an earlier screen - the same "read before the transaction opens,
/// re-priced against the live catalogue" discipline <c>CompleteSaleHandler</c> uses for a sale,
/// applied here to the refund ceiling instead of a sale price.
/// </para>
/// <para>
/// <b>A returned unit re-enters stock at today's moving-average cost, not a sale's own
/// snapshot.</b> There is no <c>sale_line.unit_cost</c> to copy - the whole premise of this flow
/// is that no <c>sale_line</c> exists. Posting at <see cref="CatalogueItem.UnitCost"/> (the
/// balance's own <c>cost_avg</c> at the moment of the return) is the only figure available and,
/// by construction of <c>MovingAverageCost.Recompute</c>, the one value that leaves the average
/// exactly where it already stood - the same non-perturbation property
/// <see cref="CreateReturnHandler"/>'s own remarks rely on for a <c>DAMAGED</c> line, achieved
/// here by a different route (an inbound movement priced at the current average, rather than no
/// movement at all).
/// </para>
/// </remarks>
public sealed class CreateUnlinkedReturnHandler : ICreateUnlinkedReturn
{
    private const string ReturnDocumentType = "RETURN";
    private const string ReturnInMovementType = "RETURN_IN";
    private const string NoOriginalBillLabel = "(unlinked return - no original bill)";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IProductLookup _catalogue;
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

    public CreateUnlinkedReturnHandler(
        IUnitOfWork unitOfWork,
        IDocumentNumberAllocator numbers,
        IProductLookup catalogue,
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
        ArgumentNullException.ThrowIfNull(catalogue);
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
        _catalogue = catalogue;
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
        CreateUnlinkedReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAtLeastOneLine(command);
        var reason = RequireReason(command);
        RefundMethodMapping.RequireSupported(command.RefundMethod);
        RequireAllowedRefundMethod(command.RefundMethod);

        var seller = RequireTheSellerIsSignedIn(command);

        // Always required, never conditional (task P2-T03 step 2) - unlike every override on
        // CreateReturnCommand, there is no rule here that might not have been broken.
        _policy.AuthoriseUnlinkedReturn(command.Override);

        // Read before the transaction opens, the same NFR-P3 discipline CreateReturnHandler keeps
        // for its own bill lookup - the write lock is for the write alone.
        var priced = await PriceLinesAsync(command, cancellationToken).ConfigureAwait(false);

        var policyText = await ReturnPolicyTextBuilder.BuildAsync(_settings, _categories, cancellationToken)
            .ConfigureAwait(false);

        var businessDate = DateOnly.FromDateTime(command.ReturnedAt.Date);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var returnNo = await _numbers
                    .AllocateAsync(ReturnDocumentType, businessDate, token)
                    .ConfigureAwait(false);

                var saleReturnId = await _returns.InsertSaleReturnAsync(
                    new NewSaleReturn(
                        returnNo,
                        OriginalSaleId: null,
                        command.ReturnedAt,
                        businessDate,
                        command.CustomerId,
                        command.UserId,
                        command.ShiftId,
                        priced.Subtotal,
                        priced.Tax,
                        priced.RestockingFee,
                        priced.TotalRefund,
                        RefundMethodMapping.ToAuditToken(command.RefundMethod),
                        AuthorisedBy: command.Override.GrantedByUserId,
                        reason),
                    token).ConfigureAwait(false);

                foreach (var line in priced.Lines)
                {
                    await _returns.InsertSaleReturnLineAsync(saleReturnId, line.ToNewSaleReturnLine(), token)
                        .ConfigureAwait(false);

                    // SELLABLE only, exactly as a linked return (SRS FR-5.8) - and a service or
                    // non-inventory product posts no movement either way (nothing to put back on
                    // a shelf that never had a shelf balance to begin with).
                    if (line.Disposition == ReturnDisposition.Sellable && !ProductTypes.PostsNoStockMovement(line.ProductType))
                    {
                        await _stock.PostAsync(
                            new StockPosting(
                                line.ProductVariantId,
                                ReturnInMovementType,
                                line.QuantityBase,

                                // Today's moving-average cost, not a sale's own snapshot - there
                                // is no sale_line to snapshot from (see the class remarks).
                                line.UnitCost,
                                ReturnDocumentType,
                                saleReturnId,
                                command.UserId,
                                command.ReturnedAt),
                            token).ConfigureAwait(false);
                    }
                }

                await _returns.InsertRefundPaymentAsync(
                    saleReturnId,
                    new NewTender(
                        RefundMethodMapping.ToTenderType(command.RefundMethod),
                        priced.TotalRefund.Negate(),
                        null,
                        command.ReturnedAt),
                    token).ConfigureAwait(false);

                // The one audit row this flow exists to guarantee (task P2-T03 step 5): action is
                // literally ReturnPolicyAuditActions.UnlinkedReturn, on the sale_return itself, so
                // a future exceptions report (P3-T08) can select every unlinked return with
                // "WHERE action = 'UNLINKED_RETURN'" and nothing more elaborate than that. This is
                // in addition to, not instead of, the OWNER_OVERRIDE_GRANTED row
                // IOwnerOverrideService.RequestAsync already wrote naming both the cashier and the
                // owner - that row proves who authorised it; this one proves which document it was.
                await _audit.RecordAsync(
                    new AuditEntry(
                        command.ReturnedAt,
                        command.UserId,
                        ReturnPolicyAuditActions.UnlinkedReturn,
                        "sale_return",
                        saleReturnId,
                        AfterJson: AuditPayload(returnNo, priced.TotalRefund),
                        Reason: reason),
                    token).ConfigureAwait(false);

                var payload = _receipts.Render(priced.ToReceipt(
                    returnNo,
                    command.ReturnedAt,
                    RefundMethodMapping.ToAuditToken(command.RefundMethod),
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
    /// Prices every requested line against the live catalogue - the only source of a price this
    /// flow has, there being no original bill. Caps each line's refund at the product's current
    /// selling price (task P2-T03 step 3) before any tax or rounding is applied to it.
    /// </summary>
    private async Task<PricedUnlinkedReturn> PriceLinesAsync(
        CreateUnlinkedReturnCommand command, CancellationToken cancellationToken)
    {
        var lines = new List<PricedUnlinkedReturnLine>(command.Lines.Count);
        var subtotal = Money.Zero;
        var tax = Money.Zero;

        foreach (var request in command.Lines)
        {
            if (!request.QuantityBase.IsPositive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(command), request.QuantityBase.Value, "A return line must ask for a positive quantity.");
            }

            var item = await _catalogue.FindByVariantIdAsync(request.ProductVariantId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Product variant {request.ProductVariantId} does not exist or is not sellable."));

            if (!request.UnitPrice.IsPositive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(command), request.UnitPrice.Amount, "A refund price must be positive.");
            }

            // The cap this whole task exists to enforce: the operator cannot type a higher figure
            // than the shelf is selling at right now (task P2-T03 step 3).
            if (request.UnitPrice > item.UnitPrice)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{request.UnitPrice} is more than {item.Description}'s current selling price of "
                    + $"{item.UnitPrice}. An unlinked return can refund at or below today's price, never above it."));
            }

            var quantity = Quantity.FromDecimal(request.QuantityBase.Value, item.BaseUomId);

            // The same LineTaxCalculator every fresh sale line uses (P1-T08) - one rounding point,
            // for this line, exactly CLAUDE.md invariant 2 (task P2-T02's own PriceReturn keeps
            // the analogous rule for a linked return, prorating instead because there it has an
            // original line total to prorate from; here there is none, so the line is priced
            // fresh, the same way a sale line would be).
            var pricing = LineTaxCalculator.Calculate(
                request.UnitPrice, quantity.Value, item.TaxRate, _settings.Tax.PricesIncludeTax, _rounding);

            subtotal += pricing.LineTotal;
            tax += pricing.Tax;

            lines.Add(new PricedUnlinkedReturnLine(
                item.ProductVariantId,
                item.ProductType,
                item.Description,
                item.UomSymbol,
                quantity,
                request.UnitPrice,
                item.UnitCost,
                pricing.Tax,
                pricing.LineTotal,
                request.Reason,
                request.Disposition));
        }

        var fee = _rounding.Round(_settings.Policy.RestockingFeeRate.Of(subtotal));
        var totalRefund = subtotal + tax - fee;

        return new PricedUnlinkedReturn(subtotal, tax, fee, totalRefund, lines);
    }

    private static void RequireAtLeastOneLine(CreateUnlinkedReturnCommand command)
    {
        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new InvalidOperationException("A return must have at least one line.");
        }
    }

    /// <summary>
    /// Mandatory, unlike <see cref="CreateReturnCommand.Reason"/> - see the class remarks and
    /// <see cref="CreateUnlinkedReturnCommand"/>'s own.
    /// </summary>
    private static string RequireReason(CreateUnlinkedReturnCommand command)
    {
        var reason = command.Reason?.Trim();

        if (string.IsNullOrEmpty(reason))
        {
            throw new InvalidOperationException(
                "An unlinked return needs a reason - there is no bill to explain why it has none.");
        }

        return reason;
    }

    private void RequireAllowedRefundMethod(RefundMethod refundMethod)
    {
        var allowed = _settings.Policy.AllowedUnlinkedRefundMethods;

        if (!allowed.Contains(refundMethod))
        {
            var allowedText = string.Join(" or ", allowed);

            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"An unlinked return may only be refunded by {allowedText} here. Change policy.allowed_unlinked_refund_methods in settings to allow {refundMethod}."));
        }
    }

    /// <inheritdoc cref="CreateReturnHandler.RequireTheSellerIsSignedIn"/>
    private AuthenticatedUser RequireTheSellerIsSignedIn(CreateUnlinkedReturnCommand command)
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

    private static string AuditPayload(string returnNo, Money totalRefund) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"return_no":"{{returnNo}}","total_refund":{{totalRefund.ToScaled()}}}""");

    private sealed record PricedUnlinkedReturn(
        Money Subtotal,
        Money Tax,
        Money RestockingFee,
        Money TotalRefund,
        IReadOnlyList<PricedUnlinkedReturnLine> Lines)
    {
        internal SaleReturnReceipt ToReceipt(
            string returnNo,
            DateTimeOffset returnedAt,
            string refundMethodToken,
            string cashierName,
            string policyText) => new(
                returnNo,
                NoOriginalBillLabel,
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

    private sealed record PricedUnlinkedReturnLine(
        long ProductVariantId,
        ProductType ProductType,
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
            SaleLineId: null,
            ProductVariantId,
            QuantityBase,
            UnitPrice,
            UnitCost,
            Tax,
            LineRefund,
            Reason,
            ReturnDispositions.ToToken(Disposition));
    }
}
