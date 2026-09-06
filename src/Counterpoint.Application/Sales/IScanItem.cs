using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Turns a scanned symbol into a bill line the cashier can see (SRS FR-3.2, NFR-P1).
/// </summary>
/// <remarks>
/// The viewmodel depends on this, not on a repository: the projection that strips cost out
/// happens here, in the Application layer, where authorisation lives (CLAUDE.md invariant 8).
/// </remarks>
public interface IScanItem
{
    /// <summary>
    /// Looks the symbol up. Returns null when nothing in the catalogue matches, which the UI
    /// shows as "not found" rather than as an error.
    /// </summary>
    public Task<ScannedItem?> ScanAsync(string barcode, CancellationToken cancellationToken = default);
}
