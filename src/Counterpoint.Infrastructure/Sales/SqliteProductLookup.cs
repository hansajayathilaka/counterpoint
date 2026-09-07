using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Reads a sellable variant off a read connection (SRS NFR-P1).
/// </summary>
/// <remarks>
/// <para>
/// A read connection, not the write one: a barcode scan must not queue behind the writer, and
/// the catalogue is not written by the sale transaction, so there is nothing uncommitted for it
/// to miss. WAL makes the read lock-free.
/// </para>
/// <para>
/// Hand-written SQL over a prepared statement rather than EF: this is the hottest lookup in the
/// system, its plan is the unique index on <c>barcode.barcode</c>, and it must stay that way
/// (docs/01_DATA_MODEL.md §12). The scaled integers come back as they are stored and become
/// <see cref="Money"/> here.
/// </para>
/// </remarks>
internal sealed class SqliteProductLookup : IProductLookup
{
    private const string SelectColumns =
        """
        SELECT pv.id, p.name, p.base_uom_id, u.symbol, pv.price, p.cost_avg, tc.rate
          FROM product_variant pv
          JOIN product    p  ON p.id  = pv.product_id
          JOIN uom        u  ON u.id  = p.base_uom_id
          JOIN tax_class  tc ON tc.id = p.tax_class_id
        """;

    private const string ByBarcodeSql =
        SelectColumns + """

          JOIN barcode b ON b.product_variant_id = pv.id
         WHERE b.barcode = $barcode AND pv.active = 1 AND p.active = 1
         LIMIT 1;
        """;

    private const string ByVariantIdSql =
        SelectColumns + """

         WHERE pv.id = $id AND pv.active = 1 AND p.active = 1
         LIMIT 1;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteProductLookup(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public Task<CatalogueItem?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        return QueryAsync(ByBarcodeSql, "$barcode", barcode, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogueItem?> FindByVariantIdAsync(
        long productVariantId,
        CancellationToken cancellationToken = default) =>
        QueryAsync(ByVariantIdSql, "$id", productVariantId, cancellationToken);

    private async Task<CatalogueItem?> QueryAsync(
        string sql,
        string parameterName,
        object parameterValue,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = parameterValue;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? Read(reader)
                : null;
        }
    }

    private static CatalogueItem Read(DbDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetInt64(2),
        reader.GetString(3),
        Money.FromScaled(reader.GetInt64(4)),
        Money.FromScaled(reader.GetInt64(5)),
        TaxRate.FromScaled(reader.GetInt64(6)));
}
