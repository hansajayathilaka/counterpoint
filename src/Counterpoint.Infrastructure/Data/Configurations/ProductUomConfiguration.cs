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
/// <c>conversion_factor = 10000</c>" is enforced in two halves, landed together in P1-T05's
/// <c>ProductUomBaseUnit0006</c>: the "at most one" half is <c>ux_product_uom_one_base</c>, the
/// partial unique index below; the "factor is exactly 10000 when it is the base" half is
/// <c>trg_product_uom_base_factor_insert</c> / <c>_update</c> (docs/01_DATA_MODEL.md §8, "The
/// product_uom base-unit guard"), because SQLite cannot add a <c>CHECK</c> to an existing table
/// without rebuilding it. The "at least one" half is not expressible as a column constraint at
/// all — the first row of a product is inserted before the second — and belongs to the
/// application-layer transaction that creates a product (P1-T05's own domain work).
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

        // Half of "exactly one base row per product" (docs/01_DATA_MODEL.md §3): at most one.
        // The other half - at least one - cannot be a column constraint, because the first
        // product_uom row for a product is inserted before there is a second to compare it to.
        entity.HasIndex(productUom => productUom.ProductId)
            .IsUnique()
            .HasDatabaseName("ux_product_uom_one_base")
            .HasFilter("is_base = 1");

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
