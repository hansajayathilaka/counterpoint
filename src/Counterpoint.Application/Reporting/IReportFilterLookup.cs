using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The names a report screen's category, brand and supplier filters offer (task P3-T06).
/// </summary>
/// <remarks>
/// <b>Not owner-only, and names only.</b> The stock-on-hand and reorder reports are open to both roles (SRS
/// section 9), so their filters cannot be fed from <c>ICategoryMaintenance</c> / <c>ISupplierMaintenance</c>,
/// which are owner-only. This returns an id and a display name - no cost, no contact details - and only
/// active rows.
/// </remarks>
public interface IReportFilterLookup
{
    /// <summary>Active categories, a child shown as "Parent / Child", by name.</summary>
    public Task<IReadOnlyList<ReportFilterOption>> ListCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Active brands, by name.</summary>
    public Task<IReadOnlyList<ReportFilterOption>> ListBrandsAsync(CancellationToken cancellationToken = default);

    /// <summary>Active suppliers, by name.</summary>
    public Task<IReadOnlyList<ReportFilterOption>> ListSuppliersAsync(CancellationToken cancellationToken = default);
}

/// <summary>One choice in a report filter.</summary>
/// <param name="Id">The category, brand or supplier id the report filter takes.</param>
/// <param name="Name">What the screen shows.</param>
public sealed record ReportFilterOption(long Id, string Name);
