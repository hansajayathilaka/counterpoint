using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Writes the one permitted update to <c>shift</c> - closing it (task P3-T03, CLAUDE.md
/// invariant 5). The sibling of <see cref="SqliteShiftWriter"/>, which only ever inserts.
/// </summary>
internal sealed class SqliteShiftCloseWriter : IShiftCloseWriter
{
    private const string OpenStatus = "OPEN";
    private const string ClosedStatus = "CLOSED";

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteShiftCloseWriter(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task CloseAsync(ShiftClose close, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(close);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Shift>()
                    .FirstOrDefaultAsync(shift => shift.Id == close.ShiftId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture, $"Shift {close.ShiftId} does not exist."));

                if (!string.Equals(row.Status, OpenStatus, StringComparison.Ordinal))
                {
                    // The database's own trg_shift_closed_is_final backs this regardless (SRS
                    // FR-8.8) - this is only the plain-language half, reached if a caller somehow
                    // got past CloseShiftHandler's own equivalent check (a race between two
                    // concurrent close attempts on this single-writer till, say).
                    throw new InvalidOperationException(
                        "This shift is already closed. A Z report can never be re-run (SRS FR-8.8).");
                }

                row.ClosedAt = close.ClosedAt;
                row.CountedCash = close.CountedCash;
                row.ExpectedCash = close.ExpectedCash;
                row.Variance = close.Variance;
                row.Status = ClosedStatus;
                row.ClosedBy = close.ClosedBy;
                row.Note = close.Note;

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }
}
