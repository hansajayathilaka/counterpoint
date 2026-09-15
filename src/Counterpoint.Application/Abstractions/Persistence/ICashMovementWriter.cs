using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes <c>cash_movement</c> - append-only, no exception (CLAUDE.md invariant 5,
/// docs/01_DATA_MODEL.md §7: <c>trg_cash_movement_no_update</c>, <c>trg_cash_movement_no_delete</c>).
/// </summary>
/// <remarks>
/// One method, the same reasoning <see cref="IShiftWriter"/> gives for its own single method:
/// a cash movement, once recorded, is never corrected in place - a mistaken entry is reversed by
/// recording the opposite direction, the same way a wrong sale is never edited, only cancelled or
/// returned.
/// </remarks>
public interface ICashMovementWriter
{
    /// <summary>Inserts the cash movement row, in the caller's transaction, and returns its id.</summary>
    public Task<long> InsertAsync(NewCashMovement movement, CancellationToken cancellationToken = default);
}
