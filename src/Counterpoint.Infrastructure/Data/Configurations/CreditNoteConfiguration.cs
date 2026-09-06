using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>credit_note</c> (docs/01_DATA_MODEL.md §6).</summary>
internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(note => note.Id);

        entity.Property(note => note.Number).IsRequired();
        entity.Property(note => note.AmountIssued).IsRequired();
        entity.Property(note => note.AmountRemaining).IsRequired();
        entity.Property(note => note.IssuedAt).IsRequired();

        // A date, not a timestamp: a credit note expires at the end of a business day.
        entity.Property(note => note.ExpiresOn).HasColumnType("TEXT");

        entity.Property(note => note.Status).IsRequired();

        entity.HasIndex(note => note.Number).IsUnique().HasDatabaseName("ux_credit_note_number");

        entity.HasOne<SaleReturn>()
            .WithMany()
            .HasForeignKey(note => note.SaleReturnId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(note => note.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_credit_note_status",
            "status IN ('ACTIVE','SPENT','EXPIRED','VOID')"));
    }
}
