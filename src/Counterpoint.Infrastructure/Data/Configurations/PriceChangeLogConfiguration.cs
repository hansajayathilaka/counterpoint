using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>price_change_log</c> (docs/01_DATA_MODEL.md §3, FR-2.17).
/// </summary>
/// <remarks>
/// No index. docs/01_DATA_MODEL.md §12 names none, and nothing reads this table on a hot path:
/// it is written when a price changes and read by an owner looking at one variant's history,
/// which is a scan of a small table. An index here would cost a write on the catalogue path to
/// serve a screen nobody opens twice a day. P1-T08 adds one if the price history screen needs it.
/// </remarks>
internal sealed class PriceChangeLogConfiguration : IEntityTypeConfiguration<PriceChangeLog>
{
    public void Configure(EntityTypeBuilder<PriceChangeLog> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(log => log.Id);

        entity.Property(log => log.OldPrice).IsRequired();
        entity.Property(log => log.NewPrice).IsRequired();
        entity.Property(log => log.ChangedAt).IsRequired();

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(log => log.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
