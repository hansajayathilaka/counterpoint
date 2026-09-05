using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>daily_sales_summary</c> (docs/01_DATA_MODEL.md §7): the day rollup a long-range
/// report reads instead of the raw tables (NFR-P5). Keyed on the business date.
/// </summary>
internal sealed class DailySalesSummaryConfiguration : IEntityTypeConfiguration<DailySalesSummary>
{
    public void Configure(EntityTypeBuilder<DailySalesSummary> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(summary => summary.BusinessDate);

        // TEXT YYYY-MM-DD, and supplied by the caller - never generated.
        entity.Property(summary => summary.BusinessDate)
            .HasColumnType("TEXT")
            .IsRequired()
            .ValueGeneratedNever();

        entity.Property(summary => summary.BuiltAt).IsRequired();
    }
}
