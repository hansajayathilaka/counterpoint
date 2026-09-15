using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Reads one <c>shift</c> row's header fields (task P3-T01).</summary>
/// <remarks>
/// Hand-written SQL over a read connection, not EF, the same split <see cref="IDashboardReader"/>
/// draws. A cash slip needs the shift's own number to print on it; a future X/Z report header
/// (P3-T02, P3-T03) needs the same fields, which is why this returns the whole
/// <see cref="ShiftSummary"/> rather than one column at a time.
/// </remarks>
public interface IShiftLookup
{
    /// <summary>The shift's header fields, or null when no shift has that id.</summary>
    public Task<ShiftSummary?> FindAsync(long shiftId, CancellationToken cancellationToken = default);
}
