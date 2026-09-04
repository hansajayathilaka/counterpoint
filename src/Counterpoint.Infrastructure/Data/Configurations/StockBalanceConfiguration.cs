using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>stock_balance</c> (docs/01_DATA_MODEL.md §4). One of three tables whose primary key is
/// not <c>id</c>: here it is the foreign key to the variant, one row per variant, so the key is
/// supplied by the caller rather than generated.
/// </summary>
internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(balance => balance.ProductVariantId);
        entity.Property(balance => balance.ProductVariantId).ValueGeneratedNever();

        entity.Property(balance => balance.QtyBase).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(balance => balance.CostAvg).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(balance => balance.UpdatedAt).IsRequired();

        entity.HasIndex(balance => balance.QtyBase).HasDatabaseName("ix_stock_balance_low");

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(balance => balance.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
