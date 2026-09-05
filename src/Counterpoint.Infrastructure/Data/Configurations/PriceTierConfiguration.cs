using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>price_tier</c> (docs/01_DATA_MODEL.md §3).</summary>
internal sealed class PriceTierConfiguration : IEntityTypeConfiguration<PriceTier>
{
    public void Configure(EntityTypeBuilder<PriceTier> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(tier => tier.Id);

        entity.Property(tier => tier.Tier).IsRequired();
        entity.Property(tier => tier.MinQty).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(tier => tier.Price).IsRequired();

        // Dates, not timestamps: a price band starts and ends on a business day.
        entity.Property(tier => tier.ValidFrom).HasColumnType("TEXT");
        entity.Property(tier => tier.ValidTo).HasColumnType("TEXT");

        // The whole of the price lookup: variant, then tier, then the quantity break.
        entity.HasIndex(tier => new { tier.ProductVariantId, tier.Tier, tier.MinQty })
            .HasDatabaseName("ix_price_tier_lookup");

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(tier => tier.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_price_tier_tier",
            "tier IN ('RETAIL','TRADE')"));
    }
}
