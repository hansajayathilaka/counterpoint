using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// Opens a shift: allocates its number, writes the row, audits it, and hands the session the
/// shift it just opened - all in one transaction (SRS FR-8.1, CLAUDE.md invariant 4).
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>AuthenticationService</c>'s one attempt: read what can be read before the
/// transaction (whether one is already open, and who is signed in), then one write transaction
/// for the number allocation, the insert and the audit row together. There is no device call and
/// no printer job here - a shift opening produces no receipt.
/// </para>
/// <para>
/// <b>C-01 is enforced twice, deliberately.</b> <c>ux_one_open_shift</c> is the database's own
/// backstop and needs nothing from this class to hold; the check here exists only to turn that
/// into a plain-language refusal (SRS UI-06) instead of a raw unique-constraint exception reaching
/// the sales screen.
/// </para>
/// </remarks>
public sealed class OpenShiftHandler : IOpenShift
{
    /// <summary>The <c>number_sequence.doc_type</c> a shift is numbered from.</summary>
    private const string ShiftDocumentType = "SHIFT";

    private const string OpenShiftAuditAction = "SHIFT_OPENED";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IShiftWriter _shifts;
    private readonly ITillSessionProvider _tillSessions;
    private readonly IAuditTrail _audit;
    private readonly Session _session;

    public OpenShiftHandler(
        IUnitOfWork unitOfWork,
        IDocumentNumberAllocator numbers,
        IShiftWriter shifts,
        ITillSessionProvider tillSessions,
        IAuditTrail audit,
        Session session)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(tillSessions);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);

        _unitOfWork = unitOfWork;
        _numbers = numbers;
        _shifts = shifts;
        _tillSessions = tillSessions;
        _audit = audit;
        _session = session;
    }

    /// <inheritdoc />
    public async Task<OpenedShift> OpenAsync(OpenShiftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        RequireTheCashierIsSignedIn(command);

        if (command.OpeningFloat.IsNegative)
        {
            throw new InvalidOperationException("The opening cash amount cannot be negative.");
        }

        // The friendlier half of C-01 (see class remarks) - read before the transaction opens,
        // the same NFR-P3 discipline every other handler in this layer keeps.
        var current = await _tillSessions.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            throw new InvalidOperationException(
                "A shift is already open. Close it before opening another.");
        }

        var businessDate = DateOnly.FromDateTime(command.OpenedAt.Date);

        var opened = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var shiftNo = await _numbers
                    .AllocateAsync(ShiftDocumentType, businessDate, token)
                    .ConfigureAwait(false);

                var shiftId = await _shifts.InsertShiftAsync(
                    new NewShift(shiftNo, command.UserId, command.OpenedAt, businessDate, command.OpeningFloat),
                    token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.OpenedAt,
                        command.UserId,
                        OpenShiftAuditAction,
                        "shift",
                        shiftId,
                        AfterJson: AuditPayload(shiftNo, command.OpeningFloat)),
                    token).ConfigureAwait(false);

                return new OpenedShift(shiftId, shiftNo, command.OpenedAt, command.OpeningFloat);
            },
            cancellationToken).ConfigureAwait(false);

        // The session this handler runs inside picks up the shift it just opened, without
        // requiring a fresh sign-in/sign-out cycle (P1-T14 "Do this" #1's remark on Session).
        _session.SetShiftId(opened.ShiftId);

        return opened;
    }

    /// <summary>
    /// Asserts that the person opening the shift is the person it will be stamped with, the same
    /// check <c>CompleteSaleHandler.RequireTheSellerIsSignedIn</c> makes for a bill (SRS FR-1.1).
    /// </summary>
    private void RequireTheCashierIsSignedIn(OpenShiftCommand command)
    {
        var cashier = _session.CurrentUser ?? throw new InvalidOperationException(
            "Nobody is signed in. A shift records who opened it, so sign in before opening one.");

        if (cashier.Id != command.UserId)
        {
            throw new InvalidOperationException(
                "This is not the signed-in user. Sign in as the cashier who is opening this shift.");
        }
    }

    /// <summary>
    /// The audit row's after-state. Written by hand, the same convention
    /// <c>CompleteSaleHandler.AuditPayload</c> keeps, so it needs no serialiser.
    /// </summary>
    private static string AuditPayload(string shiftNo, Money openingFloat) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"shift_no":"{{shiftNo}}","opening_float":{{openingFloat.ToScaled()}}}""");
}
