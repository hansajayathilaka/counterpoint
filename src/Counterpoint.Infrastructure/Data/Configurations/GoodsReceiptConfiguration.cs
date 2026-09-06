using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>goods_receipt</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(receipt => receipt.Id);

        entity.Property(receipt => receipt.GrnNo).IsRequired();
        entity.Property(receipt => receipt.ReceivedAt).IsRequired();
        entity.Property(receipt => receipt.Subtotal).IsRequired();
        entity.Property(receipt => receipt.Tax).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(receipt => receipt.OtherCost).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(receipt => receipt.Total).IsRequired();

        entity.HasIndex(receipt => receipt.GrnNo).IsUnique().HasDatabaseName("ux_grn_no");

        entity.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(receipt => receipt.SupplierId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(receipt => receipt.PurchaseOrderId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(receipt => receipt.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
