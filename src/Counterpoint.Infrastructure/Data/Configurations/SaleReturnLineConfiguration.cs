using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>sale_return_line</c> (docs/01_DATA_MODEL.md §6). APPEND ONLY.</summary>
internal sealed class SaleReturnLineConfiguration : IEntityTypeConfiguration<SaleReturnLine>
{
    public void Configure(EntityTypeBuilder<SaleReturnLine> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(line => line.Id);

        entity.Property(line => line.QtyBase).IsRequired();
        entity.Property(line => line.UnitPrice).IsRequired();
        entity.Property(line => line.UnitCost).IsRequired();
        entity.Property(line => line.Tax).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(line => line.LineRefund).IsRequired();
        entity.Property(line => line.Reason).IsRequired();
        entity.Property(line => line.Disposition).IsRequired();

        entity.HasOne<SaleReturn>()
            .WithMany()
            .HasForeignKey(line => line.SaleReturnId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<SaleLine>()
            .WithMany()
            .HasForeignKey(line => line.SaleLineId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(line => line.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_sale_return_line_qty_base", "qty_base > 0");

            table.HasCheckConstraint(
                "ck_sale_return_line_disposition",
                "disposition IN ('SELLABLE','DAMAGED')");
        });
    }
}
