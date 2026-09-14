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

        // P2-T05: redemption looks a customer's outstanding credit up by customer_id, not number,
        // whenever they do not have the slip. Cheap to carry - a plain CREATE INDEX, no rebuild -
        // for a lookup the till runs at the point of sale rather than in a report.
        entity.HasIndex(note => note.CustomerId).HasDatabaseName("ix_credit_note_customer");

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

        // P2-T05's own risk note: the redeeming UPDATE is guarded in the sale transaction
        // ("... WHERE amount_remaining >= :amt", checking rows-affected), because single-user
        // Counterpoint has no second writer to race against. This CHECK is the second line
        // docs/00's "constraints belong in the database" asks for regardless - it catches a
        // future bug in that guard (or a hand-run repair UPDATE) rather than standing in for it.
        entity.ToTable(table => table.HasCheckConstraint(
            "ck_credit_note_amount_remaining_bounds",
            "amount_remaining >= 0 AND amount_remaining <= amount_issued"));
    }
}
