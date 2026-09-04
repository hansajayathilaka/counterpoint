using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>sale_line</c> (docs/01_DATA_MODEL.md §5). APPEND ONLY apart from <c>qty_returned</c>.
/// </summary>
internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(line => line.Id);

        entity.Property(line => line.LineNo).IsRequired();
        entity.Property(line => line.Description).IsRequired();
        entity.Property(line => line.Qty).IsRequired();
        entity.Property(line => line.QtyBase).IsRequired();
        entity.Property(line => line.UnitPrice).IsRequired();
        entity.Property(line => line.Discount).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(line => line.TaxRate).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(line => line.Tax).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(line => line.LineTotal).IsRequired();
        entity.Property(line => line.UnitCost).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(line => line.QtyReturned).HasDefaultValue(0L).ValueGeneratedNever();

        entity.HasIndex(line => line.SaleId).HasDatabaseName("ix_sale_line_sale");
        entity.HasIndex(line => new { line.ProductVariantId, line.SaleId })
            .HasDatabaseName("ix_sale_line_variant");
        entity.HasIndex(line => new { line.SaleId, line.LineNo })
            .IsUnique()
            .HasDatabaseName("ux_sale_line_no");

        entity.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(line => line.SaleId)
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
