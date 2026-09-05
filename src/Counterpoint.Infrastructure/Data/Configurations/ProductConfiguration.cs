using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>product</c> (docs/01_DATA_MODEL.md §3).
/// </summary>
/// <remarks>
/// <para>
/// <c>category_id</c> and <c>brand_id</c> were plain nullable columns in the skeleton because the
/// tables they name did not exist, and with <c>PRAGMA foreign_keys = ON</c> a reference to a
/// missing table is accepted at CREATE and then fails at INSERT - a landmine, not a constraint.
/// <c>FullSchema0002</c> creates <c>category</c> and <c>brand</c>; <c>ProductForeignKeys0003</c>
/// then makes the two references real. §13 tracks the two that are still outstanding.
/// </para>
/// <para>
/// The rebuild that the foreign keys cost is why every column below carries an explicit
/// <see cref="RelationalPropertyBuilderExtensions.HasColumnOrder"/>. EF emits a
/// <c>CreateTable</c> in property declaration order, but the <em>rebuild</em> path
/// (create <c>ef_temp_product</c>, copy, drop, rename) sorts the columns alphabetically after the
/// key - so without these the physical order would silently stop matching the DDL in §3. That
/// matters because SQLite's type affinity accepts a positional
/// <c>INSERT INTO product VALUES (...)</c> written against the documented order without complaint,
/// putting a product code in <c>active</c> and a name in <c>base_uom_id</c>. A repair session with
/// <c>sqlite3</c> and a bulk import are both exactly that statement.
/// </para>
/// </remarks>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(product => product.Id);

        // The column order of docs/01_DATA_MODEL.md §3, spelled out so a table rebuild cannot
        // quietly reorder it. SchemaConformanceTests checks the file against the model.
        entity.Property(product => product.Id).HasColumnOrder(0);
        entity.Property(product => product.Code).IsRequired().HasColumnOrder(1);
        entity.Property(product => product.Name).IsRequired().HasColumnOrder(2);
        entity.Property(product => product.NameAlt).HasColumnOrder(3);
        entity.Property(product => product.CategoryId).HasColumnOrder(4);
        entity.Property(product => product.BrandId).HasColumnOrder(5);
        entity.Property(product => product.BaseUomId).HasColumnOrder(6);
        entity.Property(product => product.Type).IsRequired().HasColumnOrder(7);
        entity.Property(product => product.TaxClassId).HasColumnOrder(8);
        entity.Property(product => product.CostAvg).HasDefaultValue(Money.Zero).ValueGeneratedNever().HasColumnOrder(9);
        entity.Property(product => product.ReorderLevel).HasDefaultValue(0L).ValueGeneratedNever().HasColumnOrder(10);
        entity.Property(product => product.ReorderQty).HasDefaultValue(0L).ValueGeneratedNever().HasColumnOrder(11);
        entity.Property(product => product.Location).HasColumnOrder(12);
        entity.Property(product => product.NonReturnable).HasDefaultValue(false).ValueGeneratedNever().HasColumnOrder(13);
        entity.Property(product => product.MinSellQty).HasDefaultValue(0L).ValueGeneratedNever().HasColumnOrder(14);
        entity.Property(product => product.MaxDiscountRate).HasColumnOrder(15);
        entity.Property(product => product.WarrantyDays).HasColumnOrder(16);
        entity.Property(product => product.Notes).HasColumnOrder(17);
        entity.Property(product => product.ImagePath).HasColumnOrder(18);
        entity.Property(product => product.Active).HasDefaultValue(true).ValueGeneratedNever().HasColumnOrder(19);
        entity.Property(product => product.CreatedAt).IsRequired().HasColumnOrder(20);
        entity.Property(product => product.UpdatedAt).IsRequired().HasColumnOrder(21);

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

        entity.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(product => product.BrandId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_product_type",
            "type IN ('STANDARD','DECIMAL','SERVICE','NON_INVENTORY')"));
    }
}
