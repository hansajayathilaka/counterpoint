using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// Counter search over <c>product_search</c>, the FTS5 prefix index (SRS FR-2.11, NFR-P2,
/// docs/01_DATA_MODEL.md §3).
/// </summary>
/// <remarks>
/// A read connection and Dapper, the same choice <see cref="Counterpoint.Infrastructure.Sales.SqliteProductLookup"/>
/// makes and for the same reason: a search box a cashier is typing into must never queue behind
/// the write connection.
/// </remarks>
internal sealed class SqliteProductSearchService : IProductSearchService
{
    private const int MaxResults = 50;

    private const string SearchSql =
        """
        SELECT pv.id AS ProductVariantId,
               pv.sku AS Sku,
               p.name AS ProductName,
               br.name AS BrandName,
               c.name AS CategoryName,
               p.location AS Location,
               p.base_uom_id AS BaseUomId,
               u.symbol AS UomSymbol,
               pv.price AS UnitPrice,
               COALESCE(sb.qty_base, 0) AS QtyBase,
               CASE WHEN UPPER(p.code) = UPPER(@Raw) OR UPPER(pv.sku) = UPPER(@Raw)
                    THEN 1 ELSE 0 END AS ExactMatch
          FROM product_search
          JOIN product_variant pv ON pv.id = product_search.rowid
          JOIN product         p  ON p.id  = pv.product_id
          JOIN uom             u  ON u.id  = p.base_uom_id
          LEFT JOIN brand      br ON br.id = p.brand_id
          LEFT JOIN category   c  ON c.id  = p.category_id
          LEFT JOIN stock_balance sb ON sb.product_variant_id = pv.id
         WHERE product_search MATCH @Match AND pv.active = 1 AND p.active = 1
         ORDER BY ExactMatch DESC, bm25(product_search)
         LIMIT @MaxResults;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteProductSearchService(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var raw = query.Trim();
        var match = BuildMatchExpression(raw);

        if (match is null)
        {
            return [];
        }

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                SearchSql,
                new { Raw = raw, Match = match, MaxResults },
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            var results = new List<ProductSearchResult>(MaxResults);
            foreach (var row in rows)
            {
                results.Add(ToResult(row));
            }

            return results;
        }
    }

    /// <summary>
    /// Turns free-typed text into an FTS5 <c>MATCH</c> expression: every whitespace-separated
    /// word, quoted so any FTS5 syntax character in it is taken literally and suffixed
    /// <c>*</c> for a prefix match, joined with FTS5's implicit <c>AND</c> - "bosch dri" matches a
    /// row containing a term starting "bosch" and a term starting "dri", in either order or
    /// column. Null when nothing usable is left, so a blank or punctuation-only search returns no
    /// results instead of throwing FTS5 a syntax error or matching everything.
    /// </summary>
    private static string? BuildMatchExpression(string raw)
    {
        if (raw.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var start = 0;

        for (var i = 0; i <= raw.Length; i++)
        {
            var atEnd = i == raw.Length;
            if (!atEnd && !char.IsWhiteSpace(raw[i]))
            {
                continue;
            }

            if (i > start)
            {
                AppendToken(builder, raw[start..i]);
            }

            start = i + 1;
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static void AppendToken(StringBuilder builder, string token)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        // A double quote inside the token is escaped by doubling it, the same rule FTS5 quoted
        // strings use for themselves - so a search for O"Brien's drill cannot be mistaken for the
        // end of the token.
        builder.Append('"').Append(token.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\"*");
    }

    private static ProductSearchResult ToResult(Row row) => new(
        row.ProductVariantId,
        row.Sku,
        row.ProductName,
        row.BrandName,
        row.CategoryName,
        row.Location,
        row.BaseUomId,
        row.UomSymbol,
        Money.FromScaled(row.UnitPrice),
        Quantity.FromScaled(row.QtyBase, row.BaseUomId),
        row.ExactMatch != 0);

    /// <summary>The flat shape Dapper maps a row of <see cref="SearchSql"/> onto.</summary>
    private sealed class Row
    {
        public long ProductVariantId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;

        public string? BrandName { get; set; }

        public string? CategoryName { get; set; }

        public string? Location { get; set; }

        public long BaseUomId { get; set; }

        public string UomSymbol { get; set; } = string.Empty;

        public long UnitPrice { get; set; }

        public long QtyBase { get; set; }

        public int ExactMatch { get; set; }
    }
}
