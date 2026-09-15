using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Cash;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Cash;

/// <summary>
/// Inserts <c>cash_movement</c> (CLAUDE.md invariant 5). The same shape as
/// <see cref="Counterpoint.Infrastructure.Sales.SqliteShiftWriter"/>'s own single insert.
/// </summary>
internal sealed class SqliteCashMovementWriter : ICashMovementWriter
{
    private const string InDirection = "IN";
    private const string OutDirection = "OUT";

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteCashMovementWriter(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> InsertAsync(NewCashMovement movement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new CashMovement
                {
                    ShiftId = movement.ShiftId,
                    Direction = ToDirectionToken(movement.Direction),
                    Amount = movement.Amount,
                    Reason = movement.Reason,
                    UserId = movement.UserId,
                    OccurredAt = movement.OccurredAt,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    private static string ToDirectionToken(CashMovementDirection direction) => direction switch
    {
        CashMovementDirection.In => InDirection,
        CashMovementDirection.Out => OutDirection,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown cash movement direction."),
    };
}
