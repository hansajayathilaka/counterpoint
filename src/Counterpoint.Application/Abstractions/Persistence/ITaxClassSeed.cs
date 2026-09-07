using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Creates the shop's tax classes at first run (SRS FR-10.3, Q-02).
/// </summary>
/// <remarks>
/// <b>Seeding, not maintenance.</b> Editing, renaming and deactivating a tax class is
/// <c>P1-T04</c>'s reference-data CRUD. This exists because the first-run wizard has to put the
/// shop's classes into an empty database before any of those screens exist, and because the tax
/// regime must be a data decision taken here rather than a rate written into code.
/// </remarks>
public interface ITaxClassSeed
{
    /// <summary>
    /// Returns the id of the tax class called <paramref name="name"/>, creating it at
    /// <paramref name="rate"/> if it is absent. An existing class keeps its rate: a rate the
    /// shop has already sold against is not this service's to change.
    /// </summary>
    public Task<long> EnsureAsync(string name, TaxRate rate, CancellationToken cancellationToken = default);
}
