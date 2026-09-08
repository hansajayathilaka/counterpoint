using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Counter search: name fragment, code, SKU, brand, category or rack location, returned as the
/// cashier types (SRS FR-2.11, NFR-P2).
/// </summary>
/// <remarks>
/// A read port, exactly as <see cref="IProductLookup"/> is, and for the same reason: it is on the
/// path a cashier is standing at the till waiting on, so nothing here carries a role requirement
/// or blocks behind the write connection. The debounce and keystroke cancellation NFR-P2 asks for
/// are a UI-layer concern (P1-T09) - this interface only has to make cancelling an in-flight
/// search cheap, which the <see cref="System.Threading.CancellationToken"/> on every call does.
/// </remarks>
public interface IProductSearchService
{
    /// <summary>
    /// Finds active, sellable variants matching <paramref name="query"/>, ranked with exact code
    /// or SKU matches first, at most 50.
    /// </summary>
    /// <param name="query">
    /// What the cashier typed or scanned. Blank or whitespace-only returns an empty list rather
    /// than every product in the catalogue.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the search - what lets a caller debounce keystrokes by cancelling the previous
    /// query instead of racing it against the next one.
    /// </param>
    public Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
