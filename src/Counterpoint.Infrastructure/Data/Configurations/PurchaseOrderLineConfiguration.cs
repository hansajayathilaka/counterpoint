using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>purchase_order_line</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(line => line.Id);

        entity.Property(line => line.Qty).IsRequired();
        entity.Property(line => line.UnitCost).IsRequired();
        entity.Property(line => line.QtyReceivedBase).HasDefaultValue(0L).ValueGeneratedNever();

        entity.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderId)
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
