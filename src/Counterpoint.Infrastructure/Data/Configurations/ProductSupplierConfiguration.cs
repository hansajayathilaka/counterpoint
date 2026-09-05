using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>product_supplier</c> (docs/01_DATA_MODEL.md §3).</summary>
internal sealed class ProductSupplierConfiguration : IEntityTypeConfiguration<ProductSupplier>
{
    public void Configure(EntityTypeBuilder<ProductSupplier> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(link => link.Id);

        entity.HasIndex(link => new { link.ProductId, link.SupplierId })
            .IsUnique()
            .HasDatabaseName("ux_product_supplier");

        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(link => link.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(link => link.SupplierId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
