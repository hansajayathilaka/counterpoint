using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Answers <see cref="IReportConnectionFactory"/> by delegating to <see cref="IPosConnectionFactory"/>
/// - the seam that lets a Dapper query living in <c>Counterpoint.Reporting</c> open a read
/// connection without that project referencing <c>Counterpoint.Infrastructure</c> (CLAUDE.md
/// "Project boundaries"; see <see cref="IReportConnectionFactory"/>'s own remarks for why the
/// port exists at all).
/// </summary>
public sealed class PosReportConnectionFactory : IReportConnectionFactory
{
    private readonly IPosConnectionFactory _inner;

    public PosReportConnectionFactory(IPosConnectionFactory inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public Task<DbConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default) =>
        _inner.OpenReadConnectionAsync(cancellationToken);
}
