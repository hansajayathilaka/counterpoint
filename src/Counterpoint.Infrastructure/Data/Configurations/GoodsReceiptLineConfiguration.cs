using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>goods_receipt_line</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(line => line.Id);

        entity.Property(line => line.Qty).IsRequired();
        entity.Property(line => line.QtyBase).IsRequired();
        entity.Property(line => line.UnitCost).IsRequired();
        entity.Property(line => line.UnitCostBase).IsRequired();
        entity.Property(line => line.Tax).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(line => line.LineTotal).IsRequired();

        entity.HasIndex(line => line.GoodsReceiptId).HasDatabaseName("ix_grn_line_grn");

        entity.HasOne<GoodsReceipt>()
            .WithMany()
            .HasForeignKey(line => line.GoodsReceiptId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(line => line.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Uom>()
            .WithMany()
            .HasForeignKey(line => line.UomId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
