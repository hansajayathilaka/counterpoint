using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>product_variant</c> (docs/01_DATA_MODEL.md §3).</summary>
internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(variant => variant.Id);

        entity.Property(variant => variant.Sku).IsRequired();
        entity.Property(variant => variant.Attributes).IsRequired().HasDefaultValue("{}").ValueGeneratedNever();
        entity.Property(variant => variant.Price).IsRequired();
        entity.Property(variant => variant.Active).HasDefaultValue(true).ValueGeneratedNever();
        entity.Property(variant => variant.CreatedAt).IsRequired();

        entity.HasIndex(variant => variant.ProductId).HasDatabaseName("ix_variant_product");
        entity.HasIndex(variant => variant.Sku).IsUnique().HasDatabaseName("ux_variant_sku");

        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
