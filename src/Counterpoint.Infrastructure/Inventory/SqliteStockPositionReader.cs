using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// Reads the balance projection and recent ledger rows for the stock enquiry screen, off a read
/// connection (P1-T07, SRS FR-4, NFR-P1).
/// </summary>
/// <remarks>
/// Hand-written SQL, not EF: this is a read path, and CLAUDE.md's stack line puts hot reads on
/// Dapper's side of the split. It reads the tables by their column names directly rather than
/// through the EF entity types, which is also what keeps it out of the architecture test that
/// polices who may reference those types (the write side).
/// </remarks>
internal sealed class SqliteStockPositionReader : IStockPositionReader
{
    private const string FindSql =
        """
        SELECT qty_base, cost_avg, updated_at
          FROM stock_balance
         WHERE product_variant_id = $variantId;
        """;

    private const string RecentMovementsSql =
        """
        SELECT movement_type, qty_base, unit_cost, ref_doc_type, ref_doc_id, occurred_at
          FROM stock_movement
         WHERE product_variant_id = $variantId
         ORDER BY id DESC
         LIMIT $take;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteStockPositionReader(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<StockPosition?> FindAsync(
        long productVariantId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = FindSql;
            AddParameter(command, "$variantId", productVariantId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new StockPosition(
                productVariantId,
                Quantity.FromScaled(reader.GetInt64(0), productVariantId),
                Money.FromScaled(reader.GetInt64(1)),
                DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockMovementRecord>> RecentMovementsAsync(
        long productVariantId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = RecentMovementsSql;
            AddParameter(command, "$variantId", productVariantId);
            AddParameter(command, "$take", take);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var results = new List<StockMovementRecord>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new StockMovementRecord(
                    reader.GetString(0),
                    Quantity.FromScaled(reader.GetInt64(1), productVariantId),
                    Money.FromScaled(reader.GetInt64(2)),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture)));
            }

            return results;
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
