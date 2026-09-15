using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Cash;

/// <summary>
/// <see cref="ICashMovementService"/>: records cash in and cash out, in one transaction each, and
/// answers a shift's own history (task P3-T01 "Do this" #1, #3, #4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every write is scoped to the caller's own open shift.</b> <see cref="RecordCashInCommand.ShiftId"/>
/// and <see cref="RecordCashOutCommand.ShiftId"/> must match <see cref="ISession.ShiftId"/> - the
/// one shift C-01 permits to be open at all - so there is no code path here that posts into a shift
/// nobody is currently trading in, closed or otherwise. Closing a shift, and the database trigger
/// that would refuse a late posting once one exists, are P3-T03's own.
/// </para>
/// <para>
/// <b>The slip is an outbox row, never a printer call.</b> Exactly the discipline
/// <see cref="Counterpoint.Application.Returns.CreateUnlinkedReturnHandler"/> keeps for its own
/// receipt: <see cref="IPrintJobOutbox.EnqueueAsync"/> runs inside the same transaction as the
/// cash-movement insert, and nothing here ever calls a device (CLAUDE.md invariant 7).
/// </para>
/// </remarks>
public sealed class CashMovementService : ICashMovementService
{
    private const string CashSlipDocumentType = "CASH_SLIP";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICashMovementWriter _writer;
    private readonly ICashMovementReader _reader;
    private readonly IShiftLookup _shifts;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly ICashSlipRenderer _slips;
    private readonly ISettings _settings;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public CashMovementService(
        IUnitOfWork unitOfWork,
        ICashMovementWriter writer,
        ICashMovementReader reader,
        IShiftLookup shifts,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        ICashSlipRenderer slips,
        ISettings settings,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(slips);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _writer = writer;
        _reader = reader;
        _shifts = shifts;
        _audit = audit;
        _printJobs = printJobs;
        _slips = slips;
        _settings = settings;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<RecordedCashMovement> RecordCashInAsync(
        RecordCashInCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cashier = RequireCallerOwnsShift(command.UserId, command.ShiftId);
        var reason = RequireReason(command.Reason);
        RequirePositiveAmount(command.Amount);

        return await RecordAsync(
            CashMovementDirection.In,
            command.ShiftId,
            command.UserId,
            command.Amount,
            reason,
            command.OccurredAt,
            command.PrintSlip,
            cashier,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RecordedCashMovement> RecordCashOutAsync(
        RecordCashOutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cashier = RequireCallerOwnsShift(command.UserId, command.ShiftId);
        var reason = RequireReason(command.Reason);
        RequirePositiveAmount(command.Amount);

        var threshold = _settings.Policy.CashOutAuthorisationThreshold;
        var aboveThreshold = command.Amount > threshold;
        var now = _timeProvider.GetLocalNow();

        if (aboveThreshold && (command.OwnerOverride is null
            || !command.OwnerOverride.TryConsume(CashMovementAuditActions.CashOutAboveThreshold, now)))
        {
            throw new CashOutAuthorisationRequiredException(command.Amount, threshold);
        }

        var recorded = await RecordAsync(
            CashMovementDirection.Out,
            command.ShiftId,
            command.UserId,
            command.Amount,
            reason,
            command.OccurredAt,
            command.PrintSlip,
            cashier,
            cancellationToken,

            // Written inside the same transaction as the movement itself, in addition to (never
            // instead of) the OWNER_OVERRIDE_GRANTED row IOwnerOverrideService.RequestAsync already
            // wrote naming both the cashier and the owner - the same "one row proves who authorised
            // it, the other proves which document it was" split
            // CreateUnlinkedReturnHandler keeps for UNLINKED_RETURN (task P3-T01 "Done when": "cash
            // out above the threshold ... is audited").
            aboveThreshold ? command.OwnerOverride : null).ConfigureAwait(false);

        return recorded;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CashMovementRecord>> GetHistoryAsync(
        long shiftId, CancellationToken cancellationToken = default)
    {
        var caller = _session.CurrentUser ?? throw new NotAuthorisedException(
            "Nobody is signed in. Sign in before viewing the cash movement history.");

        if (caller.Role != Role.Owner && shiftId != _session.ShiftId)
        {
            throw new NotAuthorisedException(
                "A cashier may only see the cash movement history for the shift they are currently trading in.");
        }

        return await _reader.ListForShiftAsync(shiftId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RecordedCashMovement> RecordAsync(
        CashMovementDirection direction,
        long shiftId,
        long userId,
        Money amount,
        string reason,
        DateTimeOffset occurredAt,
        bool printSlip,
        AuthenticatedUser cashier,
        CancellationToken cancellationToken,
        OverrideToken? auditedOverride = null)
    {
        var shift = await _shifts.FindAsync(shiftId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Shift {shiftId} does not exist."));

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var movementId = await _writer.InsertAsync(
                    new NewCashMovement(shiftId, direction, amount, reason, userId, occurredAt),
                    token).ConfigureAwait(false);

                if (auditedOverride is not null)
                {
                    await _audit.RecordAsync(
                        new AuditEntry(
                            occurredAt,
                            userId,
                            CashMovementAuditActions.CashOutAboveThreshold,
                            CashMovementAuditActions.CashMovementEntityType,
                            movementId,
                            AfterJson: AuditPayload(amount, auditedOverride),
                            Reason: reason),
                        token).ConfigureAwait(false);
                }

                long? printJobId = null;
                if (printSlip)
                {
                    var payload = _slips.RenderCashMovementSlip(new CashMovementSlip(
                        movementId, shift.ShiftNo, direction, amount, reason, occurredAt, cashier.DisplayName));

                    printJobId = await _printJobs
                        .EnqueueAsync(new PrintJobRequest(CashSlipDocumentType, movementId, payload), token)
                        .ConfigureAwait(false);
                }

                return new RecordedCashMovement(movementId, direction, amount, occurredAt, printJobId);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private AuthenticatedUser RequireCallerOwnsShift(long userId, long shiftId)
    {
        var caller = _session.CurrentUser ?? throw new NotAuthorisedException(
            "Nobody is signed in. A cash movement records who recorded it, so sign in first.");

        if (caller.Id != userId)
        {
            throw new NotAuthorisedException(
                "This is not the signed-in user. Sign in as the cashier recording this movement.");
        }

        if (shiftId != _session.ShiftId)
        {
            throw new NotAuthorisedException(
                "A cash movement can only be recorded against the shift currently open on this till.");
        }

        return caller;
    }

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidOperationException(
                "A cash movement needs a reason - it goes on the drawer's own history.");
        }

        return trimmed;
    }

    private static void RequirePositiveAmount(Money amount)
    {
        if (!amount.IsPositive)
        {
            throw new InvalidOperationException("A cash movement must be for a positive amount.");
        }
    }

    /// <summary>
    /// The audit row's after-state. Written by hand, the same convention
    /// <c>OpenShiftHandler.AuditPayload</c> and <c>CreateUnlinkedReturnHandler.AuditPayload</c>
    /// keep, so it needs no serialiser.
    /// </summary>
    private static string AuditPayload(Money amount, OverrideToken ownerOverride) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"amount":{{amount.ToScaled()}},"requested_by_user_id":{{ownerOverride.RequestedByUserId}},"granted_by_user_id":{{ownerOverride.GrantedByUserId}}}""");
}
