using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Cash;

/// <summary>
/// <see cref="INoSaleDrawerService"/>: opens the drawer with no sale behind it, in one transaction
/// (SRS FR-7.7, task P3-T01 "Do this" #5).
/// </summary>
/// <remarks>
/// The same shape as <see cref="Counterpoint.Application.Returns.CreateUnlinkedReturnHandler"/>'s
/// own mandatory-override flow: <see cref="NoSaleDrawerCommand.OwnerOverride"/> is required by the
/// command itself, so there is no code path through this service that reaches the transaction
/// without a granted, unexpired, unspent <see cref="OverrideToken"/> for
/// <see cref="CashMovementAuditActions.NoSaleDrawerOpen"/>.
/// </remarks>
public sealed class NoSaleDrawerService : INoSaleDrawerService
{
    private const string CashSlipDocumentType = "CASH_SLIP";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IShiftLookup _shifts;
    private readonly IUserStore _users;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly ICashSlipRenderer _slips;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public NoSaleDrawerService(
        IUnitOfWork unitOfWork,
        IShiftLookup shifts,
        IUserStore users,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        ICashSlipRenderer slips,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(slips);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _shifts = shifts;
        _users = users;
        _audit = audit;
        _printJobs = printJobs;
        _slips = slips;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<NoSaleDrawerResult> OpenAsync(
        NoSaleDrawerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.OwnerOverride);

        var cashier = _session.CurrentUser ?? throw new NotAuthorisedException(
            "Nobody is signed in. A no-sale drawer open records who asked for it, so sign in first.");

        if (cashier.Id != command.UserId)
        {
            throw new NotAuthorisedException(
                "This is not the signed-in user. Sign in as the cashier opening the drawer.");
        }

        if (command.ShiftId != _session.ShiftId)
        {
            throw new NotAuthorisedException(
                "A no-sale drawer open can only be recorded against the shift currently open on this till.");
        }

        var now = _timeProvider.GetLocalNow();

        // Always required, never conditional (task P3-T01 "Do this" #5) - unlike
        // CashMovementService.RecordCashOutAsync's threshold, there is no rule here that might not
        // have been broken: every no-sale open is owner-authorised.
        if (!command.OwnerOverride.TryConsume(CashMovementAuditActions.NoSaleDrawerOpen, now))
        {
            throw new NotAuthorisedException(
                "An owner has to authorise a no-sale drawer open before it can happen.");
        }

        var shift = await _shifts.FindAsync(command.ShiftId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Shift {command.ShiftId} does not exist."));

        var owner = await _users.FindByIdAsync(command.OwnerOverride.GrantedByUserId, cancellationToken)
            .ConfigureAwait(false);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // The one audit row this flow exists to guarantee (SRS FR-7.7, task P3-T01 "Done
                // when": "a no-sale drawer open writes an audit row"). This is in addition to, not
                // instead of, the OWNER_OVERRIDE_GRANTED row IOwnerOverrideService.RequestAsync
                // already wrote naming both the cashier and the owner - that row proves who
                // authorised it; this one is what a future exceptions report (P3-T08) filters on
                // with "WHERE action = 'NO_SALE_DRAWER'".
                await _audit.RecordAsync(
                    new AuditEntry(
                        command.OccurredAt,
                        command.UserId,
                        CashMovementAuditActions.NoSaleDrawerOpen,
                        CashMovementAuditActions.ShiftEntityType,
                        command.ShiftId,
                        AfterJson: AuditPayload(command.OwnerOverride)),
                    token).ConfigureAwait(false);

                long? printJobId = null;
                if (command.PrintSlip)
                {
                    var payload = _slips.RenderNoSaleSlip(new NoSaleSlip(
                        shift.ShiftNo, command.OccurredAt, cashier.DisplayName, owner?.DisplayName ?? string.Empty));

                    printJobId = await _printJobs
                        .EnqueueAsync(new PrintJobRequest(CashSlipDocumentType, command.ShiftId, payload), token)
                        .ConfigureAwait(false);
                }

                return new NoSaleDrawerResult(printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string AuditPayload(OverrideToken ownerOverride) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"requested_by_user_id":{{ownerOverride.RequestedByUserId}},"granted_by_user_id":{{ownerOverride.GrantedByUserId}}}""");
}
