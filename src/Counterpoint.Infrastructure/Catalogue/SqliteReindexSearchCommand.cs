using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// Rebuilds <c>product_search</c> (SRS FR-2.11, NFR-P2, docs/01_DATA_MODEL.md §3 "the
/// contentless-delete limitation, in full").
/// </summary>
/// <remarks>
/// <para>
/// <c>'delete-all'</c>, not <c>'rebuild'</c>. <c>'rebuild'</c> repopulates an FTS5 index from a
/// backing content table, and this one is <c>content=''</c> - contentless, by design
/// (docs/01_DATA_MODEL.md §3) - so there is no content table for <c>'rebuild'</c> to read.
/// <c>'delete-all'</c> is the special command FTS5 documents for exactly a contentless table:
/// it clears every indexed row without needing the values they were indexed with, which
/// <c>'delete'</c> would.
/// </para>
/// <para>
/// The backfill that follows is deliberately the same <c>SELECT</c> the schema migration seeds a
/// freshly upgraded till with (<c>ProductSearch0004</c>) and the same one
/// <c>trg_product_search_variant_insert</c> uses on every new variant - the three must agree on
/// what an indexed row looks like, or the next <c>'delete'</c> issued against a row this command
/// wrote would corrupt the term counts.
/// </para>
/// </remarks>
internal sealed class SqliteReindexSearchCommand : IReindexSearchCommand
{
    private const string ClearSql = "INSERT INTO product_search(product_search) VALUES('delete-all');";

    private const string BackfillSql =
        """
        INSERT INTO product_search(rowid, name, name_alt, code, sku, brand, category, location)
        SELECT v.id, p.name, p.name_alt, p.code, v.sku,
               (SELECT b.name FROM brand b WHERE b.id = p.brand_id),
               (SELECT c.name FROM category c WHERE c.id = p.category_id),
               p.location
          FROM product_variant v
          JOIN product p ON p.id = v.product_id;
        """;

    private const string CountSql = "SELECT COUNT(*) FROM product_search;";

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteReindexSearchCommand(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, ClearSql, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, BackfillSql, token).ConfigureAwait(false);

                await using var count = connection.CreateCommand();
                count.Transaction = transaction;
                count.CommandText = CountSql;

                var result = await count.ExecuteScalarAsync(token).ConfigureAwait(false);
                return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
            },
            cancellationToken);

    private static async Task ExecuteAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
