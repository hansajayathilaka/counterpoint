using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="IReportFilterLookup"/>: id and name for the category, brand and supplier filters (task P3-T06).
/// Names only, active rows only - nothing here is cost-bearing, so it is registered plain.
/// </summary>
internal sealed class ReportFilterLookup : IReportFilterLookup
{
    private const string CategoriesSql =
        """
        SELECT c.id AS Id,
               CASE WHEN parent.name IS NULL THEN c.name ELSE parent.name || ' / ' || c.name END AS Name
          FROM category c
          LEFT JOIN category parent ON parent.id = c.parent_id
         WHERE c.active = 1
         ORDER BY Name;
        """;

    private const string BrandsSql = "SELECT id AS Id, name AS Name FROM brand WHERE active = 1 ORDER BY name;";

    private const string SuppliersSql = "SELECT id AS Id, name AS Name FROM supplier WHERE active = 1 ORDER BY name;";

    private readonly IReportConnectionFactory _connectionFactory;

    public ReportFilterLookup(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportFilterOption>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
        ListAsync(CategoriesSql, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportFilterOption>> ListBrandsAsync(CancellationToken cancellationToken = default) =>
        ListAsync(BrandsSql, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportFilterOption>> ListSuppliersAsync(CancellationToken cancellationToken = default) =>
        ListAsync(SuppliersSql, cancellationToken);

    private async Task<IReadOnlyList<ReportFilterOption>> ListAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<Row>(
                new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return [.. rows.Select(row => new ReportFilterOption(row.Id, row.Name))];
        }
    }

    private sealed class Row
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
