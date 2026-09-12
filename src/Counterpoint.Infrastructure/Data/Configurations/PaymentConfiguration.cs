using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>payment</c> (docs/01_DATA_MODEL.md §5). APPEND ONLY. <c>sale_return_id</c>'s foreign
/// key to <c>sale_return</c> was added in <c>PaymentSaleReturnForeignKey0007</c> (P2-T02,
/// docs/01_DATA_MODEL.md §13) - the last of the four dangling references from Skeleton0001, one
/// still deliberately unresolved: <c>sale.customer_id</c> is P5-T02's.
/// </summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(payment => payment.Id);

        entity.Property(payment => payment.TenderType).IsRequired();
        entity.Property(payment => payment.Amount).IsRequired();
        entity.Property(payment => payment.PaidAt).IsRequired();

        entity.HasIndex(payment => payment.SaleId).HasDatabaseName("ix_payment_sale");
        entity.HasIndex(payment => payment.SaleReturnId).HasDatabaseName("ix_payment_return");

        entity.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(payment => payment.SaleId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<SaleReturn>()
            .WithMany()
            .HasForeignKey(payment => payment.SaleReturnId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_payment_tender_type",
                "tender_type IN ('CASH','CARD','BANK_TRANSFER','CREDIT_NOTE','ON_ACCOUNT','CHEQUE')");

            // Exactly one of the two documents, never both and never neither.
            table.HasCheckConstraint(
                "ck_payment_one_document",
                "(sale_id IS NOT NULL) <> (sale_return_id IS NOT NULL)");
        });
    }
}
