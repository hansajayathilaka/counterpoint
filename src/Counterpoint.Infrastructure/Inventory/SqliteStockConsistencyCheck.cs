using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// The startup consistency check: samples the balance projection against the ledger it is
/// supposed to be a cache of (P1-T07, SAD §3).
/// </summary>
/// <remarks>
/// <para>
/// One query, not two hundred: <c>ORDER BY RANDOM() LIMIT $sampleSize</c> picks the sample, and a
/// <c>LEFT JOIN</c> against every one of that sample's own ledger rows sums each in the same
/// pass. This is the one place a read is allowed to sum <c>stock_movement</c> - a diagnostic
/// check, run rarely, that exists to catch the case where the projection has drifted from the
/// ledger it is meant to mirror everywhere else.
/// </para>
/// <para>
/// Diagnostic, never corrective. A mismatch is logged as a <see cref="LogLevel.Warning"/> and
/// handed back in the report; nothing here writes anything. Fixing it is
/// <see cref="IRebuildStockBalance"/>'s job, run deliberately by a person who has looked at why.
/// </para>
/// </remarks>
internal sealed partial class SqliteStockConsistencyCheck : IStockConsistencyCheck
{
    private const string SampleSql =
        """
        SELECT sb.product_variant_id AS variant_id, sb.qty_base AS projected,
               COALESCE(SUM(sm.qty_base), 0) AS ledgered
          FROM stock_balance sb
          LEFT JOIN stock_movement sm ON sm.product_variant_id = sb.product_variant_id
         WHERE sb.product_variant_id IN (
                 SELECT product_variant_id FROM stock_balance ORDER BY RANDOM() LIMIT $sampleSize)
         GROUP BY sb.product_variant_id;
        """;

    private readonly IPosConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteStockConsistencyCheck> _logger;

    public SqliteStockConsistencyCheck(
        IPosConnectionFactory connectionFactory,
        ILogger<SqliteStockConsistencyCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StockConsistencyReport> CheckAsync(
        int sampleSize = 200,
        CancellationToken cancellationToken = default)
    {
        if (sampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize), sampleSize, "The sample size must be positive.");
        }

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SampleSql;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "$sampleSize";
            parameter.Value = sampleSize;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var sampled = 0;
            var mismatches = new List<StockConsistencyMismatch>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                sampled++;

                var variantId = reader.GetInt64(0);
                var projected = reader.GetInt64(1);
                var ledgered = reader.GetInt64(2);

                if (projected != ledgered)
                {
                    mismatches.Add(new StockConsistencyMismatch(variantId, projected, ledgered));
                    VariantMismatched(variantId, projected, ledgered);
                }
            }

            var report = new StockConsistencyReport(sampled, mismatches);

            if (report.HasMismatch)
            {
                SampleMismatched(mismatches.Count, sampled);
            }

            return report;
        }
    }

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Warning,
        Message = "Stock consistency check: variant {ProductVariantId} projects {Projected} but its "
            + "ledger sums to {Ledgered}. Consider IRebuildStockBalance.")]
    private partial void VariantMismatched(long productVariantId, long projected, long ledgered);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Warning,
        Message = "Stock consistency check: {MismatchCount} of {SampledCount} sampled variants "
            + "disagreed with their ledger.")]
    private partial void SampleMismatched(int mismatchCount, int sampledCount);
}
