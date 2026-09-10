using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes <c>shift</c> - append-only apart from the close fields P3-T01 adds (CLAUDE.md
/// invariant 5, docs/01_DATA_MODEL.md §7).
/// </summary>
/// <remarks>
/// One method, deliberately: this task (P1-T14) opens a shift and never updates one. Closing a
/// shift is the one permitted update on this table (<c>status</c> OPEN to CLOSED, with the close
/// fields, settable once), and it belongs to <c>P3-T01</c>'s own writer, not this port.
/// </remarks>
public interface IShiftWriter
{
    /// <summary>Inserts the shift row and returns its id.</summary>
    public Task<long> InsertShiftAsync(NewShift shift, CancellationToken cancellationToken = default);
}
