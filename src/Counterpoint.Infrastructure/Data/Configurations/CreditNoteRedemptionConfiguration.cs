using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>credit_note_redemption</c> (docs/01_DATA_MODEL.md §6).</summary>
internal sealed class CreditNoteRedemptionConfiguration : IEntityTypeConfiguration<CreditNoteRedemption>
{
    public void Configure(EntityTypeBuilder<CreditNoteRedemption> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(redemption => redemption.Id);

        entity.Property(redemption => redemption.Amount).IsRequired();
        entity.Property(redemption => redemption.RedeemedAt).IsRequired();

        entity.HasOne<CreditNote>()
            .WithMany()
            .HasForeignKey(redemption => redemption.CreditNoteId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(redemption => redemption.SaleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
