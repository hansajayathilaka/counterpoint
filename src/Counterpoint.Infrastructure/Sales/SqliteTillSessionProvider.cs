using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Reads the one open shift and the user who opened it (C-01).
/// </summary>
/// <remarks>
/// The skeleton's stand-in for a sign-in screen. It is a read on a read connection, so it can
/// be asked before a bill is completed without taking the writer.
/// </remarks>
internal sealed class SqliteTillSessionProvider : ITillSessionProvider
{
    private const string CurrentSessionSql =
        """
        SELECT user_id, id
          FROM shift
         WHERE status = 'OPEN'
         ORDER BY id DESC
         LIMIT 1;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteTillSessionProvider(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<TillSession?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = CurrentSessionSql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new TillSession(reader.GetInt64(0), reader.GetInt64(1))
                : null;
        }
    }
}
