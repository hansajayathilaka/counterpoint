using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Exclusive use of the single write connection. Disposing it releases the gate, so every
/// acquisition must sit in a <c>using</c> or <c>await using</c>.
/// </summary>
/// <remarks>
/// One till, one writer (engineering guide §4.8). Serialising writes in-process keeps SQLite
/// off its busy-retry path entirely rather than relying on <c>busy_timeout</c> to sort it out.
/// </remarks>
public sealed class WriteConnectionLease : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate;
    private int _released;

    internal WriteConnectionLease(SemaphoreSlim gate, DbConnection connection)
    {
        _gate = gate;
        Connection = connection;
    }

    /// <summary>The long-lived write connection. Do not dispose it; dispose the lease.</summary>
    public DbConnection Connection { get; }

    public void Dispose()
    {
        // Idempotent: a double dispose must not hand out a second permit.
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _gate.Release();
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
