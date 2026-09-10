using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Inserts <c>shift</c> (CLAUDE.md invariant 5). The same shape as <see cref="FirstRunSeeder"/>'s
/// own shift seeding - this is the same insert, called from the shop's own "open shift" action
/// rather than from first run.
/// </summary>
internal sealed class SqliteShiftWriter : IShiftWriter
{
    /// <summary>The only status this writer ever inserts. Closing one to <c>CLOSED</c> is P3-T01's.</summary>
    private const string OpenStatus = "OPEN";

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteShiftWriter(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> InsertShiftAsync(NewShift shift, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shift);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Shift
                {
                    ShiftNo = shift.ShiftNo,
                    UserId = shift.UserId,
                    OpenedAt = shift.OpenedAt,
                    BusinessDate = shift.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    OpeningFloat = shift.OpeningFloat,
                    ClosedAt = null,
                    CountedCash = null,
                    ExpectedCash = null,
                    Variance = null,
                    Status = OpenStatus,
                    ClosedBy = null,
                    Note = null,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }
}
