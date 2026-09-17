using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Shifts;

/// <summary>
/// <see cref="IDailyRollupBuilder"/>: recomputes one business date via
/// <see cref="DailyRollupCalculator"/> and replaces both rollup tables' rows for it (task P3-T03
/// "Do this" #2, SRS FR-8.4, NFR-P5).
/// </summary>
internal sealed class SqliteDailyRollupBuilder : IDailyRollupBuilder
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteDailyRollupBuilder(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task RebuildAsync(
        DateOnly businessDate, DateTimeOffset builtAt, CancellationToken cancellationToken = default)
    {
        return _unitOfWork.ExecuteInTransactionAsync(
            async (connection, transaction, token) =>
            {
                var computed = await DailyRollupCalculator
                    .ComputeAsync(connection, transaction, businessDate, token)
                    .ConfigureAwait(false);

                using var context = _unitOfWork.CreateDbContext();
                var dateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var existingSummary = await context.Set<DailySalesSummary>()
                    .FirstOrDefaultAsync(row => row.BusinessDate == dateText, token)
                    .ConfigureAwait(false);

                if (existingSummary is null)
                {
                    context.Add(new DailySalesSummary
                    {
                        BusinessDate = dateText,
                        BillCount = computed.BillCount,
                        Gross = computed.Gross,
                        Discount = computed.Discount,
                        Tax = computed.Tax,
                        Net = computed.Net,
                        Cogs = computed.Cogs,
                        ReturnCount = computed.ReturnCount,
                        ReturnValue = computed.ReturnValue,
                        TenderCash = computed.TenderCash,
                        TenderCard = computed.TenderCard,
                        TenderOther = computed.TenderOther,
                        BuiltAt = builtAt,
                    });
                }
                else
                {
                    existingSummary.BillCount = computed.BillCount;
                    existingSummary.Gross = computed.Gross;
                    existingSummary.Discount = computed.Discount;
                    existingSummary.Tax = computed.Tax;
                    existingSummary.Net = computed.Net;
                    existingSummary.Cogs = computed.Cogs;
                    existingSummary.ReturnCount = computed.ReturnCount;
                    existingSummary.ReturnValue = computed.ReturnValue;
                    existingSummary.TenderCash = computed.TenderCash;
                    existingSummary.TenderCard = computed.TenderCard;
                    existingSummary.TenderOther = computed.TenderOther;
                    existingSummary.BuiltAt = builtAt;
                }

                // A full replace, not a merge (see IDailyRollupBuilder's own remarks): whatever was
                // there for this date is wrong the moment a later correction lands, so every
                // existing per-product row for the date is removed and the freshly computed set
                // takes its place. The removal is its own SaveChanges, committed to the same
                // ambient transaction but flushed before a single Add below can collide with a
                // Remove of the same (BusinessDate, ProductVariantId) key in one batch.
                var existingProducts = await context.Set<DailyProductSummary>()
                    .Where(row => row.BusinessDate == dateText)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                context.RemoveRange(existingProducts);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (var product in computed.Products)
                {
                    context.Add(new DailyProductSummary
                    {
                        BusinessDate = dateText,
                        ProductVariantId = product.ProductVariantId,
                        QtyBase = product.QtyBaseScaled,
                        Net = product.Net,
                        Cogs = product.Cogs,
                    });
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }
}
