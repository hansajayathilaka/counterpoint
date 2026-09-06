using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>stock_take_line</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class StockTakeLineConfiguration : IEntityTypeConfiguration<StockTakeLine>
{
    public void Configure(EntityTypeBuilder<StockTakeLine> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(line => line.Id);

        entity.Property(line => line.SystemQty).IsRequired();

        entity.HasIndex(line => line.StockTakeId).HasDatabaseName("ix_stock_take_line_take");

        entity.HasOne<StockTake>()
            .WithMany()
            .HasForeignKey(line => line.StockTakeId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(line => line.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
