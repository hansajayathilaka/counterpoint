using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>sale_return</c> (docs/01_DATA_MODEL.md §6). APPEND ONLY and hash chained; the
/// <c>trg_sale_return_*</c> triggers are what enforce that.
/// </summary>
internal sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(saleReturn => saleReturn.Id);

        entity.Property(saleReturn => saleReturn.ReturnNo).IsRequired();
        entity.Property(saleReturn => saleReturn.ReturnedAt).IsRequired();

        // TEXT YYYY-MM-DD, deliberately not a timestamp: it is the rollup grouping key.
        entity.Property(saleReturn => saleReturn.BusinessDate).IsRequired().HasColumnType("TEXT");

        entity.Property(saleReturn => saleReturn.Subtotal).IsRequired();
        entity.Property(saleReturn => saleReturn.Tax).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(saleReturn => saleReturn.RestockingFee).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(saleReturn => saleReturn.TotalRefund).IsRequired();
        entity.Property(saleReturn => saleReturn.RefundMethod).IsRequired();
        entity.Property(saleReturn => saleReturn.PrevHash).IsRequired();
        entity.Property(saleReturn => saleReturn.RowHash).IsRequired();

        entity.HasIndex(saleReturn => saleReturn.ReturnNo).IsUnique().HasDatabaseName("ux_return_no");
        entity.HasIndex(saleReturn => saleReturn.BusinessDate).HasDatabaseName("ix_return_date");
        entity.HasIndex(saleReturn => saleReturn.OriginalSaleId).HasDatabaseName("ix_return_sale");

        entity.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(saleReturn => saleReturn.OriginalSaleId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(saleReturn => saleReturn.ExchangeSaleId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(saleReturn => saleReturn.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(saleReturn => saleReturn.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(saleReturn => saleReturn.ShiftId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(saleReturn => saleReturn.AuthorisedBy)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_sale_return_refund_method",
            "refund_method IN ('CASH','CARD','CREDIT_NOTE','EXCHANGE','ON_ACCOUNT')"));
    }
}
