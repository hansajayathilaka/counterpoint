using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Opens a fresh, read-only connection to the local database for a report query living in
/// <c>Counterpoint.Reporting</c> (task P2-T11 and, from Phase 3 onward, the whole report suite -
/// SRS FR-4 reorder, FR-9.7, docs/04_PHASE_2_returns_inventory.md P2-T11 "Deliverables").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this port exists instead of handing out
/// <c>Counterpoint.Infrastructure.Data.IPosConnectionFactory</c> directly.</b>
/// <c>Counterpoint.Reporting</c> may reference only <c>Counterpoint.Application</c> and
/// <c>Counterpoint.Domain</c> (CLAUDE.md "Project boundaries") - the same rule that keeps
/// <c>Counterpoint.Ui</c> off <c>Counterpoint.Infrastructure</c>. A Dapper query living in
/// <c>Counterpoint.Reporting</c> still needs a live ADO.NET connection, so this is the narrow
/// seam: "open one read connection, with every PRAGMA already applied" and nothing else.
/// <c>Counterpoint.Infrastructure</c>'s own <c>PosReportConnectionFactory</c> is the only
/// implementation, built over the very same <c>IPosConnectionFactory</c> every other Dapper
/// reader in this codebase already uses, and the composition root registers it (inside
/// <c>AddCounterpointInfrastructure</c>) before <c>Counterpoint.Reporting</c>'s own report
/// queries are asked to resolve one.
/// </para>
/// <para>
/// Read-only on purpose, the same as <c>IPosConnectionFactory.OpenReadConnectionAsync</c> itself:
/// a report never competes with the single write connection a sale or a goods receipt holds
/// (CLAUDE.md invariant 9, NFR-P1 in spirit).
/// </para>
/// </remarks>
public interface IReportConnectionFactory
{
    /// <summary>Opens a fresh read connection. The caller owns and disposes it.</summary>
    public Task<DbConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default);
}
