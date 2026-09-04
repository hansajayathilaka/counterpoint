using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>sale</c> (docs/01_DATA_MODEL.md §5). APPEND ONLY apart from <c>status</c> and the two
/// cancellation columns; the triggers in migration <c>Skeleton0001</c> are what enforce that.
/// <c>customer_id</c> is a plain nullable column - see <see cref="ProductConfiguration"/> for why.
/// </summary>
internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(sale => sale.Id);

        entity.Property(sale => sale.BillNo).IsRequired();
        entity.Property(sale => sale.SoldAt).IsRequired();

        // TEXT YYYY-MM-DD, deliberately not a timestamp: it is the rollup grouping key.
        entity.Property(sale => sale.BusinessDate).IsRequired().HasColumnType("TEXT");

        entity.Property(sale => sale.Subtotal).IsRequired();
        entity.Property(sale => sale.LineDiscount).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(sale => sale.BillDiscount).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(sale => sale.Tax).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(sale => sale.Rounding).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(sale => sale.Total).IsRequired();
        entity.Property(sale => sale.Cogs).HasDefaultValue(0L).ValueGeneratedNever();
        entity.Property(sale => sale.Status).IsRequired();
        entity.Property(sale => sale.PrevHash).IsRequired();
        entity.Property(sale => sale.RowHash).IsRequired();

        entity.HasIndex(sale => sale.BusinessDate).HasDatabaseName("ix_sale_date");
        entity.HasIndex(sale => sale.ShiftId).HasDatabaseName("ix_sale_shift");
        entity.HasIndex(sale => sale.CustomerId).HasDatabaseName("ix_sale_cust");
        entity.HasIndex(sale => sale.SoldAt).HasDatabaseName("ix_sale_soldat");
        entity.HasIndex(sale => sale.BillNo).IsUnique().HasDatabaseName("ux_sale_bill_no");

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(sale => sale.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(sale => sale.ShiftId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(sale => sale.CancelledBy)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_sale_status",
            "status IN ('COMPLETED','CANCELLED')"));
    }
}
