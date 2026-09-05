using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>held_bill</c> (docs/01_DATA_MODEL.md §5).</summary>
internal sealed class HeldBillConfiguration : IEntityTypeConfiguration<HeldBill>
{
    public void Configure(EntityTypeBuilder<HeldBill> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(bill => bill.Id);

        entity.Property(bill => bill.Label).IsRequired();
        entity.Property(bill => bill.Payload).IsRequired();
        entity.Property(bill => bill.CreatedAt).IsRequired();

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(bill => bill.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
