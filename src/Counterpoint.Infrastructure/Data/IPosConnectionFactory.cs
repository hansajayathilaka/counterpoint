using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Hands out connections to the encrypted local database, already keyed and with every PRAGMA
/// from the engineering guide §4.8 applied.
/// </summary>
/// <remarks>
/// Infrastructure-facing on purpose. The Application layer talks to
/// <see cref="Counterpoint.Application.Abstractions.Persistence.IUnitOfWork"/> and to
/// repositories; it does not open connections itself.
/// </remarks>
public interface IPosConnectionFactory
{
    /// <summary>Full path of the database file this factory opens.</summary>
    public string DatabaseFilePath { get; }

    /// <summary>
    /// Waits for exclusive use of the single write connection. Dispose the lease to release it.
    /// </summary>
    public ValueTask<WriteConnectionLease> AcquireWriteConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a fresh read connection. The caller owns and disposes it.</summary>
    public Task<DbConnection> OpenReadConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a fresh connection synchronously, for callers that cannot await - notably EF Core's
    /// options builder. The caller owns and disposes it.
    /// </summary>
    public DbConnection OpenConfiguredConnection();
}
