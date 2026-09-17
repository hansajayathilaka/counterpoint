using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// <see cref="ICloseShift"/>: the Z report - the most consequential transaction after a sale (task
/// P3-T03, SRS FR-8.4, FR-8.5, FR-8.8, AC-11).
/// </summary>
/// <remarks>
/// <para>
/// <b>One transaction, five things.</b> The close writes <c>shift</c>'s close fields, rebuilds
/// both rollup tables for the shift's business date, writes an audit row and enqueues the Z
/// report's <c>print_job</c> - task P3-T03 "Do this" #2's list, "all in one". Every figure that
/// goes into the report is read before the transaction opens (the same NFR-P3 discipline
/// <c>CompleteSaleHandler</c> keeps for pricing), so the writer lock is held for the writes alone.
/// </para>
/// <para>
/// <b>Re-running or deleting a Z report is impossible through any code path.</b> This class has no
/// such method, <see cref="IShiftCloseWriter.CloseAsync"/> refuses a shift that is not currently
/// <c>OPEN</c>, and the database's own <c>trg_shift_closed_is_final</c> trigger backs that refusal
/// regardless of what this class does (SRS FR-8.8).
/// </para>
/// <para>
/// <b>The backup runs after the transaction commits, never inside it.</b>
/// <see cref="IShiftCloseBackupTrigger.RunIfEnabledAsync"/> touches the filesystem and possibly a
/// USB target - never a database call - so it is the last thing this method does, outside any
/// transaction, and never throws past this boundary (CLAUDE.md invariant 7).
/// </para>
/// </remarks>
public sealed class CloseShiftHandler : ICloseShift
{
    private const string CloseShiftAuditAction = "SHIFT_CLOSED";
    private const string ShiftEntityType = "shift";
    private const string ZReportDocumentType = "Z_REPORT";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IShiftLookup _shifts;
    private readonly IShiftCloseWriter _shiftClose;
    private readonly IXReportFiguresReader _figures;
    private readonly ICashMovementReader _cashMovements;
    private readonly IExpectedCashService _expectedCash;
    private readonly IDailyRollupBuilder _rollups;
    private readonly IAuditTrail _audit;
    private readonly IPrintJobOutbox _printJobs;
    private readonly IZReportReceiptRenderer _renderer;
    private readonly IShiftCloseBackupTrigger _backup;
    private readonly ISettings _settings;
    private readonly Session _session;

    public CloseShiftHandler(
        IUnitOfWork unitOfWork,
        IShiftLookup shifts,
        IShiftCloseWriter shiftClose,
        IXReportFiguresReader figures,
        ICashMovementReader cashMovements,
        IExpectedCashService expectedCash,
        IDailyRollupBuilder rollups,
        IAuditTrail audit,
        IPrintJobOutbox printJobs,
        IZReportReceiptRenderer renderer,
        IShiftCloseBackupTrigger backup,
        ISettings settings,
        Session session)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(shiftClose);
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(cashMovements);
        ArgumentNullException.ThrowIfNull(expectedCash);
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        _unitOfWork = unitOfWork;
        _shifts = shifts;
        _shiftClose = shiftClose;
        _figures = figures;
        _cashMovements = cashMovements;
        _expectedCash = expectedCash;
        _rollups = rollups;
        _audit = audit;
        _printJobs = printJobs;
        _renderer = renderer;
        _backup = backup;
        _settings = settings;
        _session = session;
    }

    /// <inheritdoc />
    public async Task<ClosedShift> CloseAsync(CloseShiftCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var caller = RequireCallerOwnsTheOpenShift(command.UserId, command.ShiftId);

        var shift = await _shifts.FindAsync(command.ShiftId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Shift {command.ShiftId} does not exist."));

        if (!string.Equals(shift.Status, "OPEN", StringComparison.Ordinal))
        {
            // SRS FR-8.8: a Z report is never re-run. The database's trg_shift_closed_is_final
            // backs this regardless; this is the plain-language half (SRS UI-06), the same
            // "enforced twice, deliberately" shape OpenShiftHandler's own remarks describe for
            // ux_one_open_shift.
            throw new InvalidOperationException(
                "This shift is already closed. A Z report can never be re-run (SRS FR-8.8).");
        }

        // Every read the report needs, gathered before the writer lock is taken (NFR-P3) - the
        // same discipline CompleteSaleHandler keeps for pricing a bill.
        var figures = await _figures.GetAsync(command.ShiftId, cancellationToken).ConfigureAwait(false);
        var cashMovements = await _cashMovements.ListForShiftAsync(command.ShiftId, cancellationToken)
            .ConfigureAwait(false);
        var expected = await _expectedCash.CalculateAsync(command.ShiftId, cancellationToken).ConfigureAwait(false);

        var variance = command.CountedCash - expected.ExpectedCash;
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        var threshold = _settings.Policy.ShiftCloseVarianceNoteThreshold;

        if (variance.Abs() > threshold && note is null)
        {
            throw new ShiftCloseVarianceNoteRequiredException(variance, threshold);
        }

        var report = new ZReportSummary(
            command.ShiftId,
            shift.ShiftNo,
            shift.UserId,
            shift.CashierDisplayName,
            shift.OpenedAt,
            command.ClosedAt,
            command.ClosedAt - shift.OpenedAt,
            shift.OpeningFloat,
            figures.SalesCount,
            figures.SalesValue,
            figures.ReturnsCount,
            figures.ReturnsValue,
            figures.DiscountTotal,
            figures.SalesTaxTotal,
            figures.ReturnsTaxTotal,
            figures.TaxBreakdown,
            figures.Tenders,
            cashMovements,
            expected.ExpectedCash,
            command.CountedCash,
            variance,
            caller.Id,
            caller.DisplayName,
            note);

        var printJobId = await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _shiftClose.CloseAsync(
                    new ShiftClose(
                        command.ShiftId,
                        command.ClosedAt,
                        command.CountedCash,
                        expected.ExpectedCash,
                        variance,
                        caller.Id,
                        note),
                    token).ConfigureAwait(false);

                await _rollups.RebuildAsync(shift.BusinessDate, command.ClosedAt, token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        command.ClosedAt,
                        caller.Id,
                        CloseShiftAuditAction,
                        ShiftEntityType,
                        command.ShiftId,
                        AfterJson: AuditPayload(report)),
                    token).ConfigureAwait(false);

                var payload = _renderer.Render(report);

                return await _printJobs
                    .EnqueueAsync(new PrintJobRequest(ZReportDocumentType, command.ShiftId, payload), token)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        // The till reads "no open shift" from this moment, without waiting for a fresh sign-in to
        // re-read it from the database (mirrors OpenShiftHandler.OpenAsync's own SetShiftId call).
        _session.ClearShiftId();

        // Never inside the transaction above (CLAUDE.md invariant 7): the shift is already closed
        // and durable by the time this runs, and a failed or skipped backup cannot undo that.
        var backupOutcome = await _backup.RunIfEnabledAsync(cancellationToken).ConfigureAwait(false);

        return new ClosedShift(report, printJobId, backupOutcome);
    }

    /// <summary>
    /// There is only ever one open shift (C-01), so "may close their own shift" collapses to "may
    /// close the shift currently open on this till" - the same two-part check
    /// <c>CashMovementService.RequireCallerOwnsShift</c> makes for a cash movement.
    /// </summary>
    private AuthenticatedUser RequireCallerOwnsTheOpenShift(long userId, long shiftId)
    {
        var caller = _session.CurrentUser ?? throw new NotAuthorisedException(
            "Nobody is signed in. A shift close records who closed it, so sign in first.");

        if (caller.Id != userId)
        {
            throw new NotAuthorisedException(
                "This is not the signed-in user. Sign in as the person closing this shift.");
        }

        if (shiftId != _session.ShiftId)
        {
            throw new NotAuthorisedException(
                "A shift can only be closed by the session currently trading in it.");
        }

        return caller;
    }

    /// <summary>
    /// The audit row's after-state. Written by hand, the same convention
    /// <c>OpenShiftHandler.AuditPayload</c> and <c>CashMovementService.AuditPayload</c> keep, so it
    /// needs no serialiser.
    /// </summary>
    private static string AuditPayload(ZReportSummary report) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"shift_no":"{{report.ShiftNo}}","counted_cash":{{report.CountedCash.ToScaled()}},"expected_cash":{{report.ExpectedCash.ToScaled()}},"variance":{{report.Variance.ToScaled()}}}""");
}
