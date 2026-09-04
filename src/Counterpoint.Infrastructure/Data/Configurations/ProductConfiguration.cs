using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>product</c> (docs/01_DATA_MODEL.md §3). <c>category_id</c> and <c>brand_id</c> are
/// plain nullable columns here: <c>category</c> and <c>brand</c> do not exist until P1-T01, and
/// with <c>PRAGMA foreign_keys = ON</c> a reference to a missing table fails at INSERT time, not
/// at CREATE time. §13 records which task adds the constraints.
/// </summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(product => product.Id);

        entity.Property(product => product.Code).IsRequired();
        entity.Property(product => product.Name).IsRequired();
        entity.Property(product => product.Type).IsRequired();
        entity.Property(product => product.CostAvg).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(product => product.ReorderLevel).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(product => product.ReorderQty).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(product => product.NonReturnable).HasDefaultValue(false).ValueGeneratedNever();
        entity.Property(product => product.MinSellQty).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(product => product.Active).HasDefaultValue(true).ValueGeneratedNever();
        entity.Property(product => product.CreatedAt).IsRequired();
        entity.Property(product => product.UpdatedAt).IsRequired();

        entity.HasIndex(product => product.CategoryId).HasDatabaseName("ix_product_category");
        entity.HasIndex(product => product.BrandId).HasDatabaseName("ix_product_brand");
        entity.HasIndex(product => product.Active).HasDatabaseName("ix_product_active");
        entity.HasIndex(product => product.Code).IsUnique().HasDatabaseName("ux_product_code");

        entity.HasOne<Uom>()
            .WithMany()
            .HasForeignKey(product => product.BaseUomId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<TaxClass>()
            .WithMany()
            .HasForeignKey(product => product.TaxClassId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_product_type",
            "type IN ('STANDARD','DECIMAL','SERVICE','NON_INVENTORY')"));
    }
}
