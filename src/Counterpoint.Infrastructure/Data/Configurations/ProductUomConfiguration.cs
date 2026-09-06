using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>product_uom</c> (docs/01_DATA_MODEL.md §3): the units a product sells in.
/// </summary>
/// <remarks>
/// <c>conversion_factor</c> is a scaled ratio, not a <c>Quantity</c>, so it stays a plain
/// <c>long</c>. "Exactly one row per product with <c>is_base = 1</c> and
/// <c>conversion_factor = 10000</c>" is a rule the data model records against P1-T05, with the
/// UOM conversion domain that has to satisfy it; the column constraint here is only that a
/// factor is positive.
/// </remarks>
internal sealed class ProductUomConfiguration : IEntityTypeConfiguration<ProductUom>
{
    public void Configure(EntityTypeBuilder<ProductUom> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(productUom => productUom.Id);

        entity.Property(productUom => productUom.ConversionFactor).IsRequired();
        entity.Property(productUom => productUom.IsBase).HasDefaultValue(false).ValueGeneratedNever();

        entity.HasIndex(productUom => new { productUom.ProductId, productUom.UomId })
            .IsUnique()
            .HasDatabaseName("ux_product_uom");

        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(productUom => productUom.ProductId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Uom>()
            .WithMany()
            .HasForeignKey(productUom => productUom.UomId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_product_uom_conversion_factor",
            "conversion_factor > 0"));
    }
}
