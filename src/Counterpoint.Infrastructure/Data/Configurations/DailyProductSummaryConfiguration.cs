using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>daily_product_summary</c> (docs/01_DATA_MODEL.md §7). Keyed on
/// (business date, variant) - the grain the product mix report reads.
/// </summary>
internal sealed class DailyProductSummaryConfiguration : IEntityTypeConfiguration<DailyProductSummary>
{
    public void Configure(EntityTypeBuilder<DailyProductSummary> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(summary => new { summary.BusinessDate, summary.ProductVariantId });

        entity.Property(summary => summary.BusinessDate)
            .HasColumnType("TEXT")
            .IsRequired()
            .ValueGeneratedNever();

        entity.Property(summary => summary.ProductVariantId).ValueGeneratedNever();

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(summary => summary.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
