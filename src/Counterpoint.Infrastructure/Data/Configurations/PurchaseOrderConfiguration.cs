using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>purchase_order</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(order => order.Id);

        entity.Property(order => order.PoNo).IsRequired();
        entity.Property(order => order.OrderedAt).IsRequired();
        entity.Property(order => order.Status).IsRequired();

        entity.HasIndex(order => order.PoNo).IsUnique().HasDatabaseName("ux_po_no");

        entity.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(order => order.SupplierId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_purchase_order_status",
            "status IN ('DRAFT','SENT','PARTIAL','RECEIVED','CANCELLED')"));
    }
}
